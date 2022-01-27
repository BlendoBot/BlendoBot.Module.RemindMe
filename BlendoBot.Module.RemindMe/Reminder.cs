using BlendoBot.Core.Services;
using DSharpPlus.Entities;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace BlendoBot.Module.RemindMe;

internal class Reminder : IComparable<Reminder> {
	[Key]
	public int ReminderId { get; set; }
	public DateTime Time { get; set; }
	public string Message { get; set; }
	public ulong ChannelId { get; set; }
	[NotMapped]
	public DiscordChannel Channel { get; set; }
	public ulong UserId { get; set; }
	[NotMapped]
	public DiscordUser User { get; set; }
	public ulong Frequency { get; set; } = 0ul;
	public string FrequencyString => FrequencyToReadableString(Frequency);
	public bool IsRepeating => Frequency > 0ul;

	protected Reminder() {}

	public Reminder(DateTime time, string message, ulong channelId, ulong userId, ulong frequency) {
		Time = time;
		Message = message;
		ChannelId = channelId;
		UserId = userId;
		Frequency = frequency;
		Channel = null;
		User = null;
	}

	public async Task UpdateCachedData(IDiscordInteractor discordInteractor) {
		Channel = await discordInteractor.GetChannel(this, ChannelId);
		User = await discordInteractor.GetUser(this, UserId);
	}

	public void UpdateReminderTime() {
		if (IsRepeating) {
			while (Time <= DateTime.UtcNow) {
				Time = Time.AddSeconds(Frequency);
			}
		}
	}

	public int CompareTo(Reminder other) => Time.CompareTo(other?.Time);

	public override bool Equals(object obj) {
		if (ReferenceEquals(this, obj)) {
			return true;
		}

		if (obj is null) {
			return false;
		}

		return CompareTo(obj as Reminder) == 0;
	}

	public override int GetHashCode() => Time.GetHashCode();

	public static bool operator ==(Reminder left, Reminder right) {
		if (left is null) {
			return right is null;
		}

		return left.Equals(right);
	}

	public static bool operator !=(Reminder left, Reminder right) => !(left == right);

	public static bool operator <(Reminder left, Reminder right) => left is null ? right is not null : left.CompareTo(right) < 0;

	public static bool operator <=(Reminder left, Reminder right) => left is null || left.CompareTo(right) <= 0;

	public static bool operator >(Reminder left, Reminder right) => left is not null && left.CompareTo(right) > 0;

	public static bool operator >=(Reminder left, Reminder right) => left is null ? right is null : left.CompareTo(right) >= 0;

	public static string FrequencyToReadableString(ulong frequency) {
		if (frequency % 86400 == 0) {
			ulong quantity = frequency / 86400;
			return $"{quantity} day{(quantity == 1 ? string.Empty : "s")}";
		} else if (frequency % 3600 == 0) {
			ulong quantity = frequency / 3600;
			return $"{quantity} hour{(quantity == 1 ? string.Empty : "s")}";
		} else if (frequency % 60 == 0) {
			ulong quantity = frequency / 60;
			return $"{quantity} minute{(quantity == 1 ? string.Empty : "s")}";
		} else {
			return $"{frequency} second{(frequency == 1 ? string.Empty : "s")}";
		}
	}
}
