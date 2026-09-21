namespace HanMate.App.Controls;

/// <summary>One tour shared by the pinyin page and its example page.</summary>
public sealed class PinyinGuideSession
{
    private const string SeenKey = "pinyin.gesture-guide.v2.seen";
    public static bool HasSeen => Preferences.Default.Get(SeenKey, false);
    public int Step { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;
    public void Advance(int expected) { if (IsActive && Step == expected && Step < 4) Step++; }
    public void ReturnToPinyin() { if (IsActive && Step > 2) Step = 2; }
    public void Finish() { IsActive = false; Preferences.Default.Set(SeenKey, true); }
}
