using System.ComponentModel.DataAnnotations;

namespace BlendoBot.Module.RemindMe;

internal class Settings {
	[Key]
	public int SettingsId { get; set; }
	public ulong MinimumRepeatTime { get; set; }
	public int MaximumRemindersPerPerson { get; set; }
}
