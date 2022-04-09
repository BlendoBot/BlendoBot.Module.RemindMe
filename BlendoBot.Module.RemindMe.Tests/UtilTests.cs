using Xunit;

namespace BlendoBot.Module.RemindMe.Tests;

public class UtilTests {
	[Theory]
	[InlineData("No URL", "No URL")]
	[InlineData("https://google.com/", "<https://google.com/>")]
	[InlineData("http://google.com/", "<http://google.com/>")]
	[InlineData("Go to https://github.com/ and clone the repo", "Go to <https://github.com/> and clone the repo")]
	public void DisableLinkEmbedsTests(string inputString, string expectedOutput) {
		string actualOutput = Util.DisableLinkEmbeds(inputString);
		Assert.Equal(expectedOutput, actualOutput);
	}
}
