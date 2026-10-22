namespace CnvQst;

/// <summary>
/// 新建文件工厂：产出最小合法的 v6 对话 / v9 任务骨架。
/// 结构由现有 Write() 序列化，保存前同样必须通过"重解析三检查"。
/// </summary>
public static class Creation
{
    /// <summary>新建对话（CNV v6）：根 → 一句 NPC 台词 → 结束对话；本地化表 = [说话人, 台词, 结束语]。</summary>
    public static CnvFile NewConversation(string speaker, string firstText, string endText = "")
    {
        // 头部 +0x0C 的"对话 id" = 根文本的 FNV1 哈希（官方编辑器 Conversation.Write / Copy Id 同款）
        var c = new CnvFile { Version = 6, Id = Fnv1.Hash(speaker), Valid = true };

        var root = new CnvStep { Index = 0, Type = Names.StepRoot, Depth = 0 };
        var speech = new CnvStep { Index = 1, Type = Names.StepSpeech, Depth = 1, Parent = 0 };
        var end = new CnvStep { Index = 2, Type = Names.StepEnd, Depth = 2, Parent = 1 };
        root.Children.Add(speech);
        speech.Children.Add(end);
        c.Steps.AddRange(new[] { root, speech, end });

        var lang = new LanguageBlock { Tag = new GdString("enUS") };
        lang.Texts.Add(new GdString(speaker));
        lang.Texts.Add(new GdString(firstText));
        lang.Texts.Add(new GdString(endText));
        c.Languages.Add(lang);
        return c;
    }

    /// <summary>新建任务（QST2 v9）：单 task（无目标、无事件）；本地化表 = [组名, 任务名, 标题, 描述]。</summary>
    public static QstFile NewQuest(string group, string name, string taskTitle, string taskDesc)
    {
        var q = new QstFile { Version = 9, Id = QstFile.RandomUid(), Unknown10 = 0, Valid = true };
        q.Tasks.Add(new QstTask { Uid = 1, Uid2 = 2, UnknownA8 = QstFile.RandomUid() });   // uid 官方为随机值

        var lang = new LanguageBlock { Tag = new GdString("enUS") };
        lang.Texts.Add(new GdString(group));
        lang.Texts.Add(new GdString(name));
        lang.Texts.Add(new GdString(taskTitle));
        lang.Texts.Add(new GdString(taskDesc));
        q.Languages.Add(lang);
        return q;
    }
}
