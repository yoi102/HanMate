namespace HanMate.Core.Content;

/// <summary>Stable scene tags for learning navigation; unknown tags remain visible under Other.</summary>
public static class WordCategories
{
    public const string Other = "other";
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        "common", "address", "greetings", "numbers", "quantifiers", "length", "weight", "time", "family", "food",
        "seasonings", "kitchenware", "household", "colors", "travel", "study", "nature"
    });
    public static string Scene(string category) => "word-" + category;

    // Teaching order is independent of UI language and document titles/identities.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Orders =
        new Dictionary<string, IReadOnlyList<string>>
        {
            ["common"] = Words("你好 谢谢 再见 对不起 没关系 快乐 音乐 麻木 骂人"),
            ["address"] = Words("爸爸 妈妈 爷爷 奶奶 哥哥 姐姐 女儿 叔叔 阿姨 老师 先生 女士 朋友"),
            ["greetings"] = Words("你好 您好 早上好 晚上好 谢谢 对不起 没关系 再见"),
            ["numbers"] = Words("零 一 二 三 四 五 六 七 八 九 十 百 千 万"),
            ["quantifiers"] = Words("一个 一只 一条 一本 一张 一支 一把 一件 一双 一杯 一碗 一辆"),
            ["length"] = Words("毫米 厘米 分米 米 千米 公里"),
            ["weight"] = Words("克 两 斤 千克 公斤 吨"),
            ["time"] = Words("昨天 今天 明天 分钟 先 马上"),
            ["family"] = Words("爸爸 妈妈 爷爷 奶奶 哥哥 姐姐 叔叔 阿姨 女儿 孩子"),
            ["food"] = Words("水 米饭 面包 苹果"),
            ["seasonings"] = Words("盐 糖 酱油 醋 食用油 香油 胡椒粉 花椒 八角 料酒 蚝油 辣椒酱"),
            ["kitchenware"] = Words("锅 平底锅 菜刀 砧板 锅铲 漏勺 碗 盘子 筷子 勺子 电饭锅 水壶"),
            ["household"] = Words("牙刷 牙膏 毛巾 肥皂 洗发水 沐浴露 卫生纸 纸巾 洗衣液 扫帚 拖把 垃圾桶"),
            ["colors"] = Words("红色 黄色 绿色 蓝色"),
            ["travel"] = Words("行走 公交车 车站 银行 西安"),
            ["study"] = Words("学习 老师 学生"),
            ["nature"] = Words("太阳 月亮 花儿")
        };

    public static IReadOnlyList<string> PreferredOrder(string? category) => category is null
        ? All.SelectMany(c => Orders[c]).Distinct(StringComparer.Ordinal).ToArray()
        : Orders.GetValueOrDefault(category) ?? Array.Empty<string>();

    private static IReadOnlyList<string> Words(string value) => Array.AsReadOnly(value.Split(' '));
}
