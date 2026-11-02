using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GridGame.Core.Battle
{
    /// <summary>
    /// Commands as plain JSON and back (multiplayer-ready rule 6, GG-LAW-13): the seat, the hero id and the picked tiles,
    /// nothing else. What a replay stores and what a networked session would send. Round-trips equal:
    /// <c>ToJson(FromJson(ToJson(c))) == ToJson(c)</c>.
    /// </summary>
    public static class CommandCodec
    {
        public static string ToJson(BattleCommand cmd) => ToJObject(cmd).ToString(Formatting.None);

        public static JObject ToJObject(BattleCommand cmd)
        {
            if (cmd == null) throw new ArgumentNullException(nameof(cmd));
            JObject o;
            switch (cmd)
            {
                case MoveCommand m:
                    var path = new JArray();
                    foreach (var c in m.Path) path.Add(new JArray(c.X, c.Y));
                    o = new JObject { ["type"] = "move", ["hero"] = m.HeroId, ["path"] = path, ["timeLeft"] = m.TimeLeftSeconds };
                    break;
                case UseSlotCommand u:
                    o = new JObject { ["type"] = "use", ["hero"] = u.HeroId, ["slot"] = u.Slot, ["target"] = new JArray(u.Target.X, u.Target.Y) };
                    break;
                case ShieldCommand _:
                    o = new JObject { ["type"] = "guard" };
                    break;
                default:
                    throw new NotSupportedException("No codec for command " + cmd.GetType().Name);
            }
            o["player"] = cmd.Player.Seat;
            return o;
        }

        public static BattleCommand FromJson(string json) => FromJObject(JObject.Parse(json));

        public static BattleCommand FromJObject(JObject o)
        {
            BattleCommand cmd;
            switch ((string)o["type"])
            {
                case "move":
                    var path = new List<Coord>();
                    foreach (var t in (JArray)o["path"]) path.Add(new Coord((int)t[0], (int)t[1]));
                    cmd = new MoveCommand { HeroId = (int)o["hero"], Path = path, TimeLeftSeconds = (float?)o["timeLeft"] ?? 0f };
                    break;
                case "use":
                    var target = (JArray)o["target"];
                    cmd = new UseSlotCommand { HeroId = (int)o["hero"], Slot = (int)o["slot"], Target = new Coord((int)target[0], (int)target[1]) };
                    break;
                case "guard":
                    cmd = new ShieldCommand();
                    break;
                default:
                    throw new NotSupportedException("Unknown command type " + (string)o["type"]);
            }
            cmd.Player = new PlayerId((int?)o["player"] ?? 0);
            return cmd;
        }
    }
}
