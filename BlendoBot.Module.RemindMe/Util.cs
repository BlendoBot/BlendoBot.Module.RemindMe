using System.Text.RegularExpressions;

namespace BlendoBot.Module.RemindMe;

public static class Util {
	public static string DisableLinkEmbeds(string s) {
		return Regex.Replace(s, "(http[^ ]*)", "<$1>");
	}
}
