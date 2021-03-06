using BlendoBot.Core.Entities;
using BlendoBot.Core.Module;
using BlendoBot.Core.Reactions;
using BlendoBot.Core.Utility;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace BlendoBot.Module.RemindMe;

internal class ReminderListListener : IReactionListener, IDisposable {
	private const int RemindersPerPage = 10;
	private static readonly DiscordEmoji[] DigitEmojis = new string[] { "0️⃣", "1️⃣", "2️⃣", "3️⃣", "4️⃣", "5️⃣", "6️⃣", "7️⃣", "8️⃣", "9️⃣" }.Select(e => DiscordEmoji.FromUnicode(e)).ToArray();
	private static readonly DiscordEmoji[] ArrowEmojis = new string[] { "⬅", "➡" }.Select(e => DiscordEmoji.FromUnicode(e)).ToArray();
	private static DiscordEmoji LeftArrowEmoji => ArrowEmojis[0];
	private static DiscordEmoji RightArrowEmoji => ArrowEmojis[1];
	private DiscordEmoji[] ValidReactions => DigitEmojis.Take(NumberOfRemindersOnPage).Concat(ArrowEmojis).ToArray();

	private readonly RemindMe module;
	private readonly DiscordChannel channel;
	private readonly DiscordUser user;
	private DiscordMessage message;
	private readonly IQueryable<Reminder> scopedReminders;
	private List<Reminder> remindersOnPage;
	private int scopedRemindersCount;
	private readonly TimeZoneInfo userTimeZone;
	private readonly bool showUsernames;
	private int page;
	private readonly Timer timeoutTimer;
	private bool CanGoLeftPage => page > 0;
	private bool CanGoRightPage => (page + 1) * RemindersPerPage < scopedRemindersCount;
	private int NumberOfRemindersOnPage => Math.Min(scopedRemindersCount - page * RemindersPerPage, RemindersPerPage);
	private readonly RemindMeDbContext dbContext;

	public IModule Module => module;

	internal ReminderListListener(RemindMe module, DiscordChannel channel, DiscordUser user, IQueryable<Reminder> scopedReminders, TimeZoneInfo userTimeZone, bool showUsernames, RemindMeDbContext dbContext) {
		this.module = module;
		this.user = user;
		this.channel = channel;
		message = null;
		this.scopedReminders = scopedReminders;
		remindersOnPage = new List<Reminder>();
		scopedRemindersCount = scopedReminders.Count();
		this.userTimeZone = userTimeZone;
		this.showUsernames = showUsernames;
		this.dbContext = dbContext;
		page = 0;
		timeoutTimer = new Timer(60000.0);
		timeoutTimer.Elapsed += TimeoutElapsed;
	}

	private async void TimeoutElapsed(object sender, ElapsedEventArgs e) {
		module.Logger.Log(this, new LogEventArgs {
			Message = $"RemindListListener for message {message.Id} timed out and is disposing",
			Type = LogType.Log
		});
		await DisposeSelf();
	}

	private async Task DisposeSelf() {
		if (message != null) {
			module.ModuleManager.UnregisterReactionListener(module, this, message.Id);
			foreach (DiscordEmoji emoji in ValidReactions) {
				await message.DeleteOwnReactionAsync(emoji);
			}
		}
	}

	private string GenerateMessage() {
		if (scopedRemindersCount == 0) {
			return "There are no reminders to view!";
		}
		StringBuilder sb = new();

		sb.AppendLine($"Viewing reminders {page * RemindersPerPage + 1}-{page * RemindersPerPage + NumberOfRemindersOnPage} of {scopedReminders.Count()}");
		for (int i = 0; i < NumberOfRemindersOnPage; ++i) {
			Reminder reminder = remindersOnPage[i];
			string message = reminder.Message;
			if (message.Length > 50) {
				message = $"{message[..47]}...";
			}
			sb.Append($"{DigitEmojis[i]} - ");
			if (showUsernames) {
				sb.Append($"{reminder.User.Mention} in ");
			}
			sb.AppendLine($"{reminder.Channel?.Mention ?? $"<#{reminder.ChannelId}>"} - \"{Util.DisableLinkEmbeds(message)}\"");
			sb.Append($"Alerts at {TimeZoneInfo.ConvertTime(reminder.Time, userTimeZone).ToString(RemindMe.TimeFormatString)} {UserTimeZone.UserTimeZone.GetOffsetShortString(reminder.Time, userTimeZone)}");
			if (reminder.IsRepeating) {
				sb.Append(' ');
				sb.Append($"(repeats at {TimeZoneInfo.ConvertTime(reminder.Time.AddSeconds(reminder.Frequency), userTimeZone).ToString(RemindMe.TimeFormatString)} {UserTimeZone.UserTimeZone.GetOffsetShortString(reminder.Time, userTimeZone)} every {reminder.FrequencyString})".Italics());
			}
			sb.AppendLine();
		}
		sb.Append($"React with ");
		if (CanGoLeftPage) {
			sb.Append(LeftArrowEmoji);
			if (CanGoRightPage) {
				sb.Append(" or ");
			}
		}
		if (CanGoRightPage) {
			sb.Append(RightArrowEmoji);
		}
		if (CanGoLeftPage || CanGoRightPage) {
			sb.Append(" to view other pages, or ");
		}
		sb.Append(DigitEmojis.First());
		if (NumberOfRemindersOnPage > 1) {
			sb.AppendLine($" to {DigitEmojis[NumberOfRemindersOnPage - 1]} to delete that number reminder.");
		} else {
			sb.AppendLine(" to delete that reminder.");
		}

		return sb.ToString();
	}

	public async Task CreateMessage() {
		message = await module.DiscordInteractor.Send(this, new SendEventArgs {
			Message = "Loading reminders...",
			Tag = "RemindListCreate",
			Channel = channel
		});
		await UpdateMessage();
		module.ModuleManager.RegisterReactionListener(module, this, message.Id);
		if (scopedRemindersCount == 0) {
			await DisposeSelf();
		} else {
			timeoutTimer.Start();
		}
	}

	private async Task UpdateMessage() {
		++page;
		do {
			--page;
			remindersOnPage = await scopedReminders.Skip(page * RemindersPerPage).Take(NumberOfRemindersOnPage).ToListAsync();
			scopedRemindersCount = await scopedReminders.CountAsync();
		} while (scopedRemindersCount > 0 && remindersOnPage.Count == 0 && page > 0);
		foreach (Reminder reminder in remindersOnPage) {
			await reminder.UpdateCachedData(module.DiscordInteractor, module.Logger);
		}
		await message.ModifyAsync(GenerateMessage());
		await UpdateReactions();
		if (scopedRemindersCount == 0) {
			await DisposeSelf();
		}
	}

	private async Task UpdateReactions() {
		if (message != null) {
			try {
				await message.DeleteAllReactionsAsync("Clearing all reactions for page update");
			} catch (UnauthorizedException) {
				module.Logger.Log(this, new LogEventArgs {
					Message = $"ReminderList tried clearing reactions, but couldn't because it doesn't have permission. An alternative method will be used but only this bot's reactions will be removed.",
					Type = LogType.Warning
				});
				foreach (DiscordReaction reaction in message.Reactions) {
					if (reaction.IsMe) {
						await message.DeleteOwnReactionAsync(reaction.Emoji);
					}
				}
			}
			if (CanGoLeftPage) {
				await message.CreateReactionAsync(LeftArrowEmoji);
			}
			for (int i = 0; i < NumberOfRemindersOnPage; ++i) {
				await message.CreateReactionAsync(DigitEmojis[i]);
			}
			if (CanGoRightPage) {
				await message.CreateReactionAsync(RightArrowEmoji);
			}
		}
	}

	public async Task OnReactionAdd(MessageReactionAddEventArgs e) {
		if (e.User.Id == user.Id && ValidReactions.Contains(e.Emoji)) {
			if (e.Emoji == LeftArrowEmoji) {
				if (CanGoLeftPage) {
					--page;
				}
			} else if (e.Emoji == RightArrowEmoji) {
				if (CanGoRightPage) {
					++page;
				}
			} else {
				int index = DigitEmojis.ToList().IndexOf(e.Emoji);
				Reminder reminder = remindersOnPage[index];
				await module.DeleteReminder(reminder);
				scopedRemindersCount = scopedReminders.Count();
				if (page > 0 && NumberOfRemindersOnPage <= 0) {
					--page;
				}
			}
			timeoutTimer.Stop();
			timeoutTimer.Start();
			await UpdateMessage();
		} else {
			try {
				await message.DeleteReactionAsync(e.Emoji, e.User, "Invalid reaction to reminder list");
			} catch (UnauthorizedException) {
				module.Logger.Log(this, new LogEventArgs {
					Message = $"ReminderList tried clearing reactions, but couldn't because it doesn't have permission. The user must clear their own reaction.",
					Type = LogType.Warning
				});
			}
		}
	}

	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing) {
		if (disposing) {
			if (timeoutTimer != null) {
				timeoutTimer.Dispose();
				dbContext.Dispose();
			}
		}
	}
}
