using System.Globalization;
using Foldspace.Core.Net;
using Foldspace.Core.Protocol;
using Foldspace.Core.Transfer;
using Foldspace.Localization;

namespace Foldspace.Core.Tests;

public class LocalizationTests
{
    public static TheoryData<string> Languages => new() { "en", "zh-Hant", "zh-Hans" };

    private static Strings For(string language) => Strings.Create(language, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("zh-Hant", "zh-Hant")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-SG", "zh-Hans")]
    [InlineData("zh-Hans", "zh-Hans")]
    [InlineData("en-US", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData("de-DE", "en")]
    public void Auto_follows_the_windows_display_language(string system, string expected) =>
        Assert.Equal(expected, Strings.Create("auto", CultureInfo.GetCultureInfo(system)).CultureName);

    [Theory]
    [InlineData("en", "zh-TW", "en")]
    [InlineData("zh-Hans", "zh-TW", "zh-Hans")]
    [InlineData("ZH-HANT", "en-US", "zh-Hant")]
    [InlineData("klingon", "zh-CN", "zh-Hans")] // 不認得的值當作 auto
    [InlineData(null, "zh-TW", "zh-Hant")]
    public void Settings_override_wins(string? setting, string system, string expected) =>
        Assert.Equal(expected, Strings.Create(setting, CultureInfo.GetCultureInfo(system)).CultureName);

    /// <summary>每個代碼在每種語言都要有翻譯，不能直接顯示代碼名稱。</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_code_has_a_translation(string language)
    {
        var t = For(language);
        foreach (var issue in Enum.GetValues<FileIssue>())
            AssertTranslated(t.FileIssueText(issue), issue);
        foreach (var reason in Enum.GetValues<RejectReason>())
            AssertTranslated(t.RejectText(reason, 2_000_000, 1_000_000), reason);
        foreach (var reason in Enum.GetValues<CancelReason>())
            AssertTranslated(t.CancelText(reason), reason);
        foreach (var reason in Enum.GetValues<PairRejectReason>())
            AssertTranslated(t.PairRejectText(reason), reason);
        foreach (var issue in Enum.GetValues<PeerIssue>().Where(i => i != PeerIssue.None))
            AssertTranslated(t.PeerIssueText(issue, 52500, "detail", "9.9.9"), issue);
        foreach (var state in Enum.GetValues<PeerState>())
            AssertTranslated(t.Status(new PeerStatus(state, PeerIssue.PortInUse, "HOST", Port: 52500)), state, allowSameWord: language == "en");
        foreach (var outcome in Enum.GetValues<TestOutcome>())
            AssertTranslated(t.TestResult(new TestConnectionResult { Outcome = outcome, Endpoint = "1.2.3.4:52500", PeerHostname = "HOST" }), outcome);
        foreach (var result in Enum.GetValues<PairingResult>())
            AssertTranslated(t.Pairing(new PairingOutcome(result, "HOST", PairRejectReason.Busy)), result);
        foreach (var issue in Enum.GetValues<JobIssue>())
        {
            var note = new JobNote(issue) { Cause = CancelReason.Peer, Rejection = RejectReason.Busy, Count = 3, PeerIssue = PeerIssue.PortInUse };
            AssertTranslated(t.Job(note, TransferDirection.Send), issue);
            AssertTranslated(t.Job(note, TransferDirection.Receive), issue);
        }
        foreach (var state in Enum.GetValues<TransferJobState>())
            AssertTranslated(t.JobHeadline(state, 42), state, allowSameWord: language == "en");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Parameters_appear_in_the_text(string language)
    {
        var t = For(language);
        Assert.Contains("HOST", t.Status(new PeerStatus(PeerState.Connected, PeerHostname: "HOST")));
        Assert.Contains("52500", t.PeerIssueText(PeerIssue.PortInUse, 52500));
        Assert.Contains("9.9.9", t.PeerIssueText(PeerIssue.VersionIncompatible, peerVersion: "9.9.9"));
        Assert.Contains("1.9 MB", t.RejectText(RejectReason.InsufficientSpace, 2_000_000, 1_000_000));
        Assert.Contains("7", t.Job(new JobNote(JobIssue.Interrupted) { Cause = CancelReason.Peer, Count = 7 }, TransferDirection.Receive));
        Assert.Contains("report.pdf", t.Items("report.pdf", 3));
        Assert.Equal("report.pdf", t.Items("report.pdf", 1));
    }

    [Fact]
    public void Simplified_chinese_uses_mainland_terms()
    {
        var t = For("zh-Hans");
        var all = string.Join("\n", t.SettingsTitle, t.LabelLocalPort, t.GroupReceiveFolder, t.TrayRecreateShortcut, t.LabelNetworkAdapter);
        Assert.Contains("设置", all);
        Assert.Contains("端口", all);
        Assert.Contains("文件夹", all);
        Assert.Contains("快捷方式", all);
        Assert.DoesNotContain("資料夾", all);
        Assert.DoesNotContain("連接埠", all);
    }

    /// <param name="allowSameWord">英文的「Offline」「Failed」這類狀態，正確的顯示文字剛好和代碼名稱相同。</param>
    private static void AssertTranslated(string text, Enum code, bool allowSameWord = false)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), $"{code.GetType().Name}.{code} has no text");
        if (!allowSameWord)
            Assert.True(code.ToString() != text, $"{code.GetType().Name}.{code} is shown as its code name");
    }
}
