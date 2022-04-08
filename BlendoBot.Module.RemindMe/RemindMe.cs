using BlendoBot.Core.Entities;
using BlendoBot.Core.Module;
using BlendoBot.Core.Services;
using DSharpPlus.Exceptions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlendoBot.Module.RemindMe;

[Module(Guid = "com.biendeo.blendobot.module.remindme", Name = "Remind Me", Author = "Biendeo", Version = "2.0.0", Url = "https://github.com/BlendoBot/BlendoBot.Module.RemindMe")]
[ModuleDependency(DependsOn = typeof(UserTimeZone.UserTimeZone))]
public class RemindMe : IModule, IDisposable {
	public RemindMe(IAdminRepository adminRepository, IDiscordInteractor discordInteractor, IFilePathProvider filePathProvider, ILogger logger, IModuleManager moduleManager) {
		AdminRepository = adminRepository;
		DiscordInteractor = discordInteractor;
		FilePathProvider = filePathProvider;
		Logger = logger;
		ModuleManager = moduleManager;

		RemindMeCommand = new RemindMeCommand(this);
	}

	internal ulong GuildId { get; private set; }

	internal readonly RemindMeCommand RemindMeCommand;

	internal readonly IAdminRepository AdminRepository;
	internal readonly IDiscordInteractor DiscordInteractor;
	internal readonly IFilePathProvider FilePathProvider;
	internal readonly ILogger Logger;
	internal readonly IModuleManager ModuleManager;

	private Thread reminderThread;
	private bool isTerminating;

	internal Reminder NextReminder;

	internal const string TimeFormatString = "d/MM/yyyy h:mm:ss tt";

	public async Task<bool> Startup(ulong guildId) {
		GuildId = guildId;

		using RemindMeDbContext dbContext = RemindMeDbContext.Get(this);
		await dbContext.Database.EnsureCreatedAsync();

		foreach (Reminder existingReminder in dbContext.Reminders.Where(r => r.Time < DateTime.UtcNow)) {
			await ReminderElapsed(existingReminder, true);
		}
		NextReminder = await dbContext.Reminders.OrderBy(r => r.Time).FirstOrDefaultAsync();

		isTerminating = false;
		reminderThread = new(new ThreadStart(ReminderThread));
		reminderThread.Start();

		return ModuleManager.RegisterCommand(this, RemindMeCommand, out _);
	}

	public void Dispose() {
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing) {
		if (disposing) {
			isTerminating = true;
			reminderThread.Join();
		}
	}

	private async void ReminderThread() {
		while (!isTerminating) {
			try {
				if (NextReminder != null && NextReminder.Time < DateTime.UtcNow) {
					await ReminderElapsed(NextReminder, false);
					using RemindMeDbContext dbContext = RemindMeDbContext.Get(this);
					NextReminder = await dbContext.Reminders.OrderBy(r => r.Time).FirstOrDefaultAsync();
				}
			} catch (Exception e) {
				Logger.Log(this, new LogEventArgs {
					Type = LogType.Error,
					Message = $"Received exception when polling for next reminder:\n{e}"
				});
			}
			Thread.Sleep(500);
		}
	}

	private async Task ReminderElapsed(Reminder r, bool sleptIn) {
		await r.UpdateCachedData(DiscordInteractor, Logger);
		if (r.Channel == null) {
			Logger.Log(this, new LogEventArgs {
				Type = LogType.Warning,
				Message = $"Tried sending a reminder message {r.Message} which should've sent at {r.Time}, but an error was encountered with the channel! This tried to send to user {r.UserId} in channel {r.ChannelId}."
			});
		} else {
			StringBuilder sb = new();
			if (!sleptIn) {
				sb.AppendLine($"{r.User.Mention} wanted to know this message now!");
			} else {
				sb.AppendLine($"I just woke up and forgot to send {r.User.Mention} this alert on time!");
			}
			sb.AppendLine(r.Message);
			if (r.IsRepeating) {
				await UpdateReminderTime(r);
				TimeZoneInfo userTimeZone = ModuleManager.GetModule<UserTimeZone.UserTimeZone>(GuildId).GetUserTimeZone(r.User);
				sb.AppendLine($"This reminder will repeat on {TimeZoneInfo.ConvertTime(r.Time, userTimeZone).ToString(TimeFormatString)} {UserTimeZone.UserTimeZone.GetOffsetShortString(r.Time, userTimeZone)}.");
			}
			try {
				await DiscordInteractor.Send(this, new SendEventArgs {
					Message = sb.ToString(),
					Channel = r.Channel,
					Tag = "ReminderAlert"
				});
			} catch (UnauthorizedException) {
				Logger.Log(this, new LogEventArgs {
					Type = LogType.Warning,
					Message = $"Tried sending a reminder message {r.Message} which should've sent at {r.Time}, but a 403 was received! This tried to send to user {r.UserId} in channel {r.ChannelId}."
				});
			} catch (NotFoundException) {
				Logger.Log(this, new LogEventArgs {
					Type = LogType.Warning,
					Message = $"Tried sending a reminder message {r.Message} which should've sent at {r.Time}, but a 404 was received! This tried to send to user {r.UserId} in channel {r.ChannelId}."
				});
			}
		}
		if (!r.IsRepeating) {
			await DeleteReminder(r);
		}
	}

	private async Task UpdateReminderTime(Reminder reminder) {
		using RemindMeDbContext dbContext = RemindMeDbContext.Get(this);
		reminder.UpdateReminderTime();
		dbContext.Reminders.Single(r => r.ReminderId == reminder.ReminderId).Time = reminder.Time;
		await dbContext.SaveChangesAsync();
	}

	internal async Task DeleteReminder(Reminder reminder) {
		using RemindMeDbContext dbContext = RemindMeDbContext.Get(this);
		// This check is just in case a reminder is triggered, but a list view later triggers it to be deleted.
		if (dbContext.Reminders.Any(r => r.ReminderId == reminder.ReminderId)) {
			dbContext.Reminders.Remove(reminder);
		}
		await dbContext.SaveChangesAsync();
		if (reminder == NextReminder) {
			NextReminder = await dbContext.Reminders.OrderBy(r => r.Time).FirstOrDefaultAsync();
		}
	}
}
