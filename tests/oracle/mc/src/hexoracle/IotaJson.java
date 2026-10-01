package hexoracle;

import at.petrak.hexcasting.api.casting.iota.BooleanIota;
import at.petrak.hexcasting.api.casting.iota.DoubleIota;
import at.petrak.hexcasting.api.casting.iota.EntityIota;
import at.petrak.hexcasting.api.casting.iota.GarbageIota;
import at.petrak.hexcasting.api.casting.iota.Iota;
import at.petrak.hexcasting.api.casting.iota.IotaType;
import at.petrak.hexcasting.api.casting.iota.ListIota;
import at.petrak.hexcasting.api.casting.iota.NullIota;
import at.petrak.hexcasting.api.casting.iota.PatternIota;
import at.petrak.hexcasting.api.casting.iota.Vec3Iota;
import at.petrak.hexcasting.api.casting.math.HexDir;
import at.petrak.hexcasting.api.casting.math.HexPattern;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonPrimitive;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.class_1297;
import net.minecraft.class_1299;
import net.minecraft.class_243;

/**
 * iota 和用例 JSON 互转。格式两边共用（移植版的对拍程序读同一份），见 tests/oracle/README.md：
 *   {"t":"num","v":1.5}（NaN / 正负无穷写成字符串 "NaN" / "Infinity" / "-Infinity"）
 *   {"t":"bool","v":true}  {"t":"null"}  {"t":"garbage"}  {"t":"vec","v":[x,y,z]}
 *   {"t":"list","v":[...]}  {"t":"pat","dir":"EAST","angles":"qaq"}
 *   {"t":"entity","ref":"self"}（施法者本人）或 {"t":"entity","type":"minecraft:pig"}
 *   {"t":"other","class":"ContinuationIota","snbt":"..."}（只出现在输出里：其余类型照原版存档格式原样给出）
 */
final class IotaJson {
    private IotaJson() {
    }

    static Iota parse(JsonElement e, class_1297 caster) {
        JsonObject o = e.getAsJsonObject();
        String t = o.get("t").getAsString();
        switch (t) {
            case "num":
                return new DoubleIota(num(o.get("v")));
            case "bool":
                return new BooleanIota(o.get("v").getAsBoolean());
            case "null":
                return new NullIota();
            case "garbage":
                return new GarbageIota();
            case "vec": {
                JsonArray v = o.getAsJsonArray("v");
                return new Vec3Iota(new class_243(num(v.get(0)), num(v.get(1)), num(v.get(2))));
            }
            case "list": {
                List<Iota> items = new ArrayList<>();
                for (JsonElement x : o.getAsJsonArray("v")) items.add(parse(x, caster));
                return new ListIota(items);
            }
            case "pat":
                return new PatternIota(pattern(o));
            case "entity":
                if ("self".equals(o.has("ref") ? o.get("ref").getAsString() : "")) return new EntityIota(caster);
                throw new IllegalArgumentException("只支持 ref=self 的实体");
            default:
                throw new IllegalArgumentException("不认识的 iota 类型：" + t);
        }
    }

    static HexPattern pattern(JsonObject o) {
        return HexPattern.Companion.fromAngles(o.get("angles").getAsString(), HexDir.valueOf(o.get("dir").getAsString()));
    }

    static JsonElement write(Iota i, class_1297 caster) {
        JsonObject o = new JsonObject();
        if (i instanceof DoubleIota d) {
            o.addProperty("t", "num");
            o.add("v", num(d.getDouble()));
        } else if (i instanceof BooleanIota b) {
            o.addProperty("t", "bool");
            o.addProperty("v", b.getBool());
        } else if (i instanceof NullIota) {
            o.addProperty("t", "null");
        } else if (i instanceof GarbageIota) {
            o.addProperty("t", "garbage");
        } else if (i instanceof Vec3Iota v) {
            class_243 p = v.getVec3();
            JsonArray a = new JsonArray();
            a.add(num(p.field_1352));
            a.add(num(p.field_1351));
            a.add(num(p.field_1350));
            o.addProperty("t", "vec");
            o.add("v", a);
        } else if (i instanceof ListIota l) {
            JsonArray a = new JsonArray();
            for (Iota x : l.getList()) a.add(write(x, caster));
            o.addProperty("t", "list");
            o.add("v", a);
        } else if (i instanceof PatternIota p) {
            o.addProperty("t", "pat");
            o.addProperty("dir", p.getPattern().getStartDir().name());
            o.addProperty("angles", p.getPattern().anglesSignature());
        } else if (i instanceof EntityIota en) {
            o.addProperty("t", "entity");
            if (en.getEntity().method_5667().equals(caster.method_5667())) o.addProperty("ref", "self");
            else o.addProperty("type", class_1299.method_5890(en.getEntity().method_5864()).toString());
        } else {
            o.addProperty("t", "other");
            o.addProperty("class", i.getClass().getSimpleName());
            o.addProperty("snbt", IotaType.serialize(i).toString());
        }
        return o;
    }

    static JsonArray writeAll(Iterable<Iota> iotas, class_1297 caster) {
        JsonArray a = new JsonArray();
        for (Iota x : iotas) a.add(write(x, caster));
        return a;
    }

    private static double num(JsonElement e) {
        if (e.isJsonPrimitive() && e.getAsJsonPrimitive().isString()) return Double.parseDouble(e.getAsString());
        return e.getAsDouble();
    }

    private static JsonElement num(double d) {
        if (Double.isNaN(d)) return new JsonPrimitive("NaN");
        if (Double.isInfinite(d)) return new JsonPrimitive(d > 0 ? "Infinity" : "-Infinity");
        return new JsonPrimitive(d);
    }
}
