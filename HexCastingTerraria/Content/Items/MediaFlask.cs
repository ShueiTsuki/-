using HexCastingTerraria.Core.Media;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace HexCastingTerraria.Content.Items;

/// <summary>
/// 媒质瓶（源项目 ItemMediaBattery，「媒质之瓶 / phial of media」）。
///
/// 与原版一致：
///   - **每瓶自带存量与上限**，不可堆叠；
///   - 施法时它是**最先扣**的媒质来源（优先级 4000），按量扣，不是整瓶消耗；
///   - 只能用法术「制作试剂瓶」（craft/battery）把地上的一堆媒质装进空瓶得到，存量 = 上限 = 装进去的量；
///   - 用「重新充能」（recharge）补到上限；
///   - 没有合成配方，也不能喝。
///
/// 注意：这里曾经是「可合成、可堆叠 99、右键喝掉把媒质倒进玩家媒质池」—— 原版没有玩家媒质池，已按原版改回。
/// </summary>
public class MediaFlask : ModItem
{
    /// <summary>当前存量。</summary>
    public long Media { get; private set; }

    /// <summary>上限（制作时装了多少就是多少）。</summary>
    public long MaxMedia { get; private set; }

    /// <summary>源项目 ItemMediaHolder.withMedia(stack, media, max)。</summary>
    public void SetMedia(long media, long max)
    {
        MaxMedia = System.Math.Max(0, max);
        Media = System.Math.Clamp(media, 0, MaxMedia);
    }

    /// <summary>扣掉一部分（施法付费）。返回实际扣掉的量。</summary>
    public long Withdraw(long amount)
    {
        long take = System.Math.Clamp(amount, 0, Media);
        Media -= take;
        return take;
    }

    /// <summary>补充（重新充能）。返回实际装进去的量。</summary>
    public long Insert(long amount)
    {
        long put = System.Math.Clamp(amount, 0, MaxMedia - Media);
        Media += put;
        return put;
    }

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
    {
        tooltips.Add(new TooltipLine(Mod, "HexFlaskMedia",
            $"媒质：{MediaConstants.Format(Media)} / {MediaConstants.Format(MaxMedia)}"));
    }

    // 源项目在物品栏里画一条媒质条（ItemMediaHolder 的 barWidth / barColor）
    public override void PostDrawInInventory(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch, Vector2 position,
        Rectangle frame, Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        if (MaxMedia <= 0) return;
        float fill = (float)Media / MaxMedia;
        var tex = Client.HexPixel.Value;
        float w = 26f * Main.inventoryScale, h = 2f * Main.inventoryScale;
        var at = position + new Vector2(-w / 2f, 12f * Main.inventoryScale);
        spriteBatch.Draw(tex, at, null, new Color(20, 16, 30, 220), 0f, Vector2.Zero, new Vector2(w, h + 1), Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
        spriteBatch.Draw(tex, at, null, Client.HexClientSystem.MediaBarColor(fill), 0f, Vector2.Zero, new Vector2(w * fill, h), Microsoft.Xna.Framework.Graphics.SpriteEffects.None, 0f);
    }

    public override void SaveData(Terraria.ModLoader.IO.TagCompound tag)
    {
        tag["media"] = Media;
        tag["maxMedia"] = MaxMedia;
    }

    public override void LoadData(Terraria.ModLoader.IO.TagCompound tag)
    {
        long media = tag.TryGet("media", out long m) ? m : 0;
        // 旧存档的瓶子没有上限字段：上限就按当时装的量
        long max = tag.TryGet("maxMedia", out long mx) ? mx : media;
        SetMedia(media, max);
    }

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write(Media);
        writer.Write(MaxMedia);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        long media = reader.ReadInt64();
        SetMedia(media, reader.ReadInt64());
    }

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 1;
        Item.value = Item.buyPrice(silver: 20);
        Item.rare = ItemRarityID.LightPurple;
    }
}
