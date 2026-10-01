using Terraria.Localization;

namespace HexCastingTerraria.Addons.HexDebug.Game;

/// <summary>HexDebug 的文字（上游 lang 文件的官方译文；调试面板的标签是移植版自己的，跟随游戏语言）。</summary>
internal static class HexDebugText
{
    public static string Get(string key, params object[] args)
        => Language.GetTextValue("Mods.HexCastingTerraria.HexDebug." + key, args);

    /// <summary>上游 displayThread：「线程 N」或「线程 N（调试杖名）」。</summary>
    public static string Thread(int threadId, string? envName)
        => envName is null ? Get("ThreadInactive", threadId) : Get("ThreadActive", threadId, envName);

    public static string StepMode(Core.StepMode mode) => Get("StepMode." + mode);
}
