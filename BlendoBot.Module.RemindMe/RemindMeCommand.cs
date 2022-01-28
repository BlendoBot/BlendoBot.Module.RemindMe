using BlendoBot.Core.Command;
using BlendoBot.Core.Entities;
using BlendoBot.Core.Module;
using BlendoBot.Core.Utility;
using DSharpPlus.EventArgs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BlendoBot.Module.RemindMe;

internal class RemindMeCommand : ICommand {
	public RemindMeCommand(RemindMe module) {
		this.module = module;
	}

	private readonly RemindMe module;
	public IModule Module => module;

	public string Guid => "remindme.command";
	public string DesiredTerm => "remind";
	public string Description => "Gives you friendly reminders of things at a later point in time";
	public Dictionary<string, string> Usage => new() {
		{ "at [date and/or time] (every [repeat interval]) to [message]", "Prepare a reminder at a point in time." },
		{ "in [timespan] (every [repeat interval]) to [message]", "Prepare a reminder after an amount of time."},
		{ "list", "Brings up an interactive view of all your reminders, and lets you delete individual ones." },
		{ "admin list", $"For BlendoBot admins only, the same as {"list".Code()} but shows all users' reminders." },
		{ "admin minrepeattime [num]", "For BlendoBot admins only, sets the minimum time a reminder can be repeated (0 disables the repeat feature)." },
		{ "admin maxreminders [num]", "For BlendoBot admins only, sets the maximum number of reminders one user can set." },
		{ "Valid date formats", $"{"dd/mm/yyyy".Code()} (e.g. {"1/03/2020".Code()})\n{"dd/mm/yy".Code()} (e.g. {"20/05/19".Code()})\n{"dd/mm".Code()} (e.g. {"30/11".Code()} (the year is implied))" },
		{ "Valid time formats", $"{"hh:mm:ss".Code()} (e.g. {"13:40:00".Code()})\n{"hh:mm".Code()} (e.g. {"04:20".Code()})" },
		{ "Valid timespan formats", $"{"hh:mm:ss".Code()} (e.g. {"1:20:00".Code()})\n{"mm:ss".Code()} (e.g. {"00:01".Code()})" },
		{ "Valid repeat interval formats", $"{"(second(s) | minute(s) | hour(s) | day(s))".Code()} (e.g. {"every hour".Code()})\n{"x (second(s) | minute(s) | hour(s) | day(s))".Code() } (e.g. {"every 4 days".Code()})" },
		{ "Note", $"All date/time strings are interpreted as UTC time unless you have configured a timezone with {module.ModuleManager.GetModule<UserTimeZone.UserTimeZone>(module.GuildId).CommandTermWithPrefix.Code()}.\nThe output is always formatted as {RemindMe.TimeFormatString.Code()}." }
	};

	public async Task OnMessage(MessageCreateEventArgs e, string[] tokenizedMessage) {
		// Try and decipher the output.
		using RemindMeDbContext dbContext = RemindMeDbContext.Get(module);

		// The list functionality is separate.
		if (tokenizedMessage.Length >= 1 && tokenizedMessage[0].ToLower() == "list") {
			await SendListMessage(e, false);
			return;
		} else if (tokenizedMessage.Length >= 1 && tokenizedMessage[0].ToLower() == "admin") {
			await SendAdminMessage(e, tokenizedMessage[1..]);
			return;
		}

		// If the user has too many reminders, don't let them make another.
		if (dbContext.Reminders.Where(r => r.UserId == e.Author.Id).Count() >= dbContext.Settings.MaximumRemindersPerPerson) {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = $"You have too many outstanding reminders! Please use {$"{module.ModuleManager.GetCommandTermWithPrefix(this)} list".Code()} to delete some.",
				Channel = e.Channel,
				Tag = "ReminderErrorTooManyReminders"
			});
			return;
		}

		TimeZoneInfo userTimeZone = module.ModuleManager.GetModule<UserTimeZone.UserTimeZone>(module.GuildId).GetUserTimeZone(e.Author);

		// Try and look for the "every" index.
		int everyIndex = 0;
		while (everyIndex < tokenizedMessage.Length && tokenizedMessage[everyIndex].ToLower() != "every") {
			++everyIndex;
		}

		// Try and look for the "to" index.
		int toIndex = 0;
		while (toIndex < tokenizedMessage.Length && tokenizedMessage[toIndex].ToLower() != "to") {
			++toIndex;
		}

		// If the every index exists after the to, then it's part of the message and shouldn't count.
		if (everyIndex >= toIndex) {
			everyIndex = -1;
		}

		// If the every index actually exists but the feature is disabled, users should know.
		if (everyIndex > -1 && dbContext.Settings.MinimumRepeatTime == 0ul) {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = "The \"every\" feature is disabled, please try again without that part of the reminder.",
				Channel = e.Channel,
				Tag = "ReminderErrorEveryDisabled"
			});
			return;
		}

		if (toIndex == tokenizedMessage.Length) {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = "Incorrect syntax, make sure you use the word \"to\" after you indicate the time you want the reminder!",
				Channel = e.Channel,
				Tag = "ReminderErrorNoTo"
			});
			return;
		} else if (toIndex == tokenizedMessage.Length - 1) {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = "Incorrect syntax, make sure you type a message after that \"to\"!",
				Channel = e.Channel,
				Tag = "ReminderErrorNoMessage"
			});
			return;
		}

		// Now decipher the time.
		DateTime foundTime = DateTime.UtcNow;
		string[] writtenTime = tokenizedMessage.Skip(1).Take((everyIndex > -1 ? everyIndex : toIndex) - 1).ToArray();
		if (tokenizedMessage[0] == "at") {
			bool successfulFormat = true;
			DateTime foundDate = DateTime.UtcNow.Add(userTimeZone.BaseUtcOffset).Date;
			TimeSpan foundTimeSpan = new();
			bool didUserInputDate = false;
			bool didUserInputTime = false;
			if (writtenTime.Length > 0 && writtenTime.Length <= 2) {
				foreach (string item in writtenTime) {
					if (item.Contains(':') && !didUserInputTime) {
						try {
							int[] splitDigits = item.Split(":").Select(s => int.Parse(s)).ToArray();
							if (splitDigits.Length == 2) {
								foundTimeSpan = new TimeSpan(splitDigits[0], splitDigits[1], 0);
							} else if (splitDigits.Length == 3) {
								foundTimeSpan = new TimeSpan(splitDigits[0], splitDigits[1], splitDigits[2]);
							} else {
								successfulFormat = false;
							}
						} catch (FormatException) {
							successfulFormat = false;
						}
						didUserInputTime = true;
					} else if (item.Contains('/') && !didUserInputDate) {
						try {
							int[] splitDigits = item.Split("/").Select(s => int.Parse(s)).ToArray();
							if (splitDigits.Length == 2) {
								foundDate = new DateTime(foundTime.Year, splitDigits[1], splitDigits[0]);
							} else if (splitDigits.Length == 3) {
								if (splitDigits[2] < 100) {
									splitDigits[2] += 2000; // Won't work when we hit 2100 but that shouldn't be hard to spot.
								}
								foundDate = new DateTime(splitDigits[2], splitDigits[1], splitDigits[0]);
							} else {
								successfulFormat = false;
							}
						} catch (FormatException) {
							successfulFormat = false;
						}
						didUserInputDate = true;
					} else {
						successfulFormat = false;
					}
				}
				// Basically, interpet the date that the user wants, then factor the timezone to UTC.
				if (userTimeZone.IsInvalidTime(DateTime.SpecifyKind(foundDate + foundTimeSpan, DateTimeKind.Unspecified))) {
					await module.DiscordInteractor.Send(this, new SendEventArgs {
						Message = $"The date/time you input is an invalid time for your timezone!",
						Channel = e.Channel,
						Tag = "ReminderErrorInvalidTimeZoneTime"
					});
					return;
				}
				foundTime = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(foundDate + foundTimeSpan, DateTimeKind.Unspecified), userTimeZone);

				/*
				 * I want the time the user specified in the time zone specifically at the time they specify.
				 */
			} else {
				successfulFormat = false;
			}
			if (!successfulFormat) {
				await module.DiscordInteractor.Send(this, new SendEventArgs {
					Message = $"The date/time you input could not be parsed! See {module.ModuleManager.GetHelpTermForCommand(this).Code()} for how to format your date/time!",
					Channel = e.Channel,
					Tag = "ReminderErrorInvalidTime"
				});
				return;
			}
		} else if (tokenizedMessage[0] == "in") {
			bool successfulFormat = true;
			if (writtenTime.Length == 1) {
				try {
					int[] splitDigits = writtenTime[0].Split(":").Select(s => int.Parse(s)).ToArray();
					TimeSpan timespan = new();
					if (splitDigits.Length == 2) {
						timespan = new TimeSpan(0, splitDigits[0], splitDigits[1]);
					} else if (splitDigits.Length == 3) {
						timespan = new TimeSpan(splitDigits[0], splitDigits[1], splitDigits[2]);
					} else {
						successfulFormat = false;
					}
					if (successfulFormat) {
						foundTime += timespan;
					}
				} catch (FormatException) {
					successfulFormat = false;
				}
			} else {
				successfulFormat = false;
			}
			if (!successfulFormat) {
				await module.DiscordInteractor.Send(this, new SendEventArgs {
					Message = $"The timespan you input could not be parsed! See {module.ModuleManager.GetHelpTermForCommand(this).Code()} for how to format your timespan!",
					Channel = e.Channel,
					Tag = "ReminderErrorInvalidTimespan"
				});
				return;
			}
		} else {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = "Incorrect syntax, make sure you use the word \"in\" or \"at\" to specify a time for the reminder!",
				Channel = e.Channel,
				Tag = "ReminderErrorNoAt"
			});
			return;
		}
		if (foundTime < DateTime.UtcNow) {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = $"The time you input was parsed as {TimeZoneInfo.ConvertTime(foundTime, userTimeZone).ToString(RemindMe.TimeFormatString)} {UserTimeZone.UserTimeZone.GetOffsetShortString(foundTime, userTimeZone)}, which is in the past! Make your time a little more specific!",
				Channel = e.Channel,
				Tag = "ReminderErrorPastTime"
			});
			return;
		}
		// Handle the every portion.
		ulong frequency = 0ul;
		if (everyIndex > -1) {
			// At this point it is guaranteed the every is before the to, which means there are at least two terms here.
			bool successfulFormat = true;
			bool quantityParsed = ulong.TryParse(tokenizedMessage[everyIndex + 1], out frequency);
			if (!quantityParsed) {
				frequency = 1;
			}
			int scaleIndex = everyIndex + 1 + (quantityParsed ? 1 : 0);
			string scaleText = tokenizedMessage[scaleIndex].ToLower();
			if (scaleText.EndsWith('s')) {
				scaleText = scaleText[0..^1];
			}
			switch (scaleText) {
				case "second":
					break;
				case "minute":
					frequency *= 60ul;
					break;
				case "hour":
					frequency *= 3600ul;
					break;
				case "day":
					frequency *= 86400ul;
					break;
				default:
					successfulFormat = false;
					break;
			}
			ulong minimumRepeatTime = dbContext.Settings.MinimumRepeatTime;
			if (frequency < minimumRepeatTime) {
				await module.DiscordInteractor.Send(this, new SendEventArgs {
					Message = $"The every frequency you input was less than {Reminder.FrequencyToReadableString(minimumRepeatTime)}! Please use a longer frequency!",
					Channel = e.Channel,
					Tag = "ReminderErrorInvalidEveryTooLow"
				});
				return;
			}
			if (!successfulFormat) {
				await module.DiscordInteractor.Send(this, new SendEventArgs {
					Message = $"The every frequency you input could not be parsed! See {module.ModuleManager.GetHelpTermForCommand(this).Code()} for how to format your repeat interval!",
					Channel = e.Channel,
					Tag = "ReminderErrorInvalidEvery"
				});
				return;
			}
		}

		// Finally extract the message.
		string message = string.Join(' ', tokenizedMessage.Skip(toIndex + 1));

		// Make the reminder.
		Reminder reminder = new(TimeZoneInfo.ConvertTimeToUtc(foundTime), message, e.Channel.Id, e.Author.Id, frequency) {
			Channel = e.Channel,
			User = e.Author
		};
		if (module.NextReminder == null || module.NextReminder.Time > reminder.Time) {
			module.NextReminder = reminder;
		}
		dbContext.Reminders.Add(reminder);
		await dbContext.SaveChangesAsync();

		await module.DiscordInteractor.Send(this, new SendEventArgs {
			Message = $"Okay, I'll tell you this message at {TimeZoneInfo.ConvertTime(foundTime, userTimeZone).ToString(RemindMe.TimeFormatString)} {UserTimeZone.UserTimeZone.GetOffsetShortString(foundTime, userTimeZone)}",
			Channel = e.Channel,
			Tag = "ReminderConfirm"
		});
	}

	private async Task SendListMessage(MessageCreateEventArgs e, bool isAdmin) {
		RemindMeDbContext dbContext = RemindMeDbContext.Get(module); // Its scope is managed by the ReminderListListener.
		TimeZoneInfo userTimeZone = module.ModuleManager.GetModule<UserTimeZone.UserTimeZone>(module.GuildId).GetUserTimeZone(e.Author);
		IQueryable<Reminder> scopedReminders;
		if (isAdmin) {
			scopedReminders = dbContext.Reminders.OrderBy(r => r.Time);
		} else {
			scopedReminders = dbContext.Reminders.Where(r => r.UserId == e.Author.Id).OrderBy(r => r.Time);
		}
		await Task.CompletedTask;
		ReminderListListener listListener = new(module, e.Channel, e.Author, scopedReminders, userTimeZone, isAdmin, dbContext);
		await listListener.CreateMessage();
	}

	private async Task SendAdminMessage(MessageCreateEventArgs e, string[] remainingMessage) {
		if (await module.AdminRepository.IsUserAdmin(this, e.Guild, e.Channel, e.Author)) {
			using RemindMeDbContext dbContext = RemindMeDbContext.Get(module);
			if (remainingMessage.Length >= 1 && remainingMessage[0].ToLower() == "list") {
				await SendListMessage(e, true);
			} else if (remainingMessage.Length >= 2 && remainingMessage[0].ToLower() == "minrepeattime") {
				if (ulong.TryParse(remainingMessage[1], out ulong minRepeatTime)) {
					if (minRepeatTime == 0ul) {
						dbContext.Settings.MinimumRepeatTime = minRepeatTime;
						await dbContext.SaveChangesAsync();
						await module.DiscordInteractor.Send(this, new SendEventArgs {
							Message = $"Repeat reminders are now disabled. New messages can't use the \"every\" feature.",
							Channel = e.Channel,
							Tag = "ReminderSetMinFrequencyOff"
						});
					} else if (minRepeatTime < 300ul) {
						await module.DiscordInteractor.Send(this, new SendEventArgs {
							Message = $"Cannot set a frequency to less than 5 minutes.",
							Channel = e.Channel,
							Tag = "ReminderSetMinFrequencyOff"
						});
					} else {
						dbContext.Settings.MinimumRepeatTime = minRepeatTime;
						await dbContext.SaveChangesAsync();
						await module.DiscordInteractor.Send(this, new SendEventArgs {
							Message = $"Repeat reminders are enabled and the minimum repeat interval is now {Reminder.FrequencyToReadableString(minRepeatTime)}.",
							Channel = e.Channel,
							Tag = "ReminderSetMinFrequencyOn"
						});
					}
				} else {
					await module.DiscordInteractor.Send(this, new SendEventArgs {
						Message = $"Could not parse your new minimum frequency.",
						Channel = e.Channel,
						Tag = "ReminderErrorInvalidMinFrequency"
					});
				}
			} else if (remainingMessage.Length >= 2 && remainingMessage[0].ToLower() == "maxreminders") {
				if (int.TryParse(remainingMessage[1], out int maxReminders)) {
					if (maxReminders > 0) {
						dbContext.Settings.MaximumRemindersPerPerson = maxReminders;
						await dbContext.SaveChangesAsync();
						await module.DiscordInteractor.Send(this, new SendEventArgs {
							Message = $"Maximum reminders per person is now {maxReminders}.",
							Channel = e.Channel,
							Tag = "ReminderSetMaxReminders"
						});
					} else {
						await module.DiscordInteractor.Send(this, new SendEventArgs {
							Message = $"Maximum reminders cannot be negative.",
							Channel = e.Channel,
							Tag = "ReminderErrorMaxRemindersNegative"
						});
					}
				} else {
					await module.DiscordInteractor.Send(this, new SendEventArgs {
						Message = $"Could not parse your new minimum frequency.",
						Channel = e.Channel,
						Tag = "ReminderErrorInvalidMinFrequency"
					});
				}
			} else {
				await module.DiscordInteractor.Send(this, new SendEventArgs {
					Message = $"Incorrect syntax! See {module.ModuleManager.GetHelpTermForCommand(this).Code()} for what you can do with this command!",
					Channel = e.Channel,
					Tag = "ReminderErrorInvalidAdminCommand"
				});
			}
		} else {
			await module.DiscordInteractor.Send(this, new SendEventArgs {
				Message = $"Only administrators can use this feature!",
				Channel = e.Channel,
				Tag = "ReminderNotAuthorised"
			});
		}
	}
}
