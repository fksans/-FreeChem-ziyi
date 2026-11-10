using System;
using System.Text;

namespace FreeChem
{
    /// <summary>
    /// v3 入口。Init/OnEnable 在主菜单运行：不读存档、不访问游戏内管理器。
    /// 反应规则靠 ReactionPatch（Harmony，加载器自动应用）；这里只负责日志和进度 tooltip。
    /// </summary>
    public class ModEntry : IMod
    {
        ModLog log;

        public void Init(ModManifest manifest)
        {
            log = new ModLog(manifest);
            GameApi.Bind(log);
            log.Log("FreeChem v3 initializing: " + RecipeTable.All.Length + " recipes.");
        }

        public void OnEnable()
        {
            ModHook.OnModItemDirectoryInit += RegisterItems;
            ModHook.OnCreateTooltipLate += OnTooltip;
            log.Log("FreeChem v3 enabled.");
        }

        public void OnDisable()
        {
            ModHook.OnModItemDirectoryInit -= RegisterItems;
            ModHook.OnCreateTooltipLate -= OnTooltip;
            log.Log("FreeChem v3 disabled.");
        }

        void RegisterItems(ModItemDirectory dir)
        {
            dir.Add(RecipeTable.SludgeItemId, GameApi.BuildSludgeItem);
        }

        /// <summary>容器正在反应时，在 tooltip 里显示配方名和进度条（RECON 第 5 节：保底的进度显示）。</summary>
        void OnTooltip(RichTextBuilder builder, GameItem item)
        {
            try
            {
                if (!GameApi.IsLiquidContainer(item)) return;
                var st = GameApi.ReadState(item);
                if (st.Idle) return;

                Recipe recipe = null;
                foreach (var r in RecipeTable.All) if (r.Id == st.RecipeId) { recipe = r; break; }
                if (recipe == null) return;

                float temp = GameApi.GetTemperature(item);
                var band = recipe.BandAt(temp) ?? recipe.Bands[0];
                bool paused = recipe.BandAt(temp) == null;

                builder.AddLine(L("fc3_tip_header", L("recipe_" + recipe.Id)));
                builder.AddLine(L("fc3_tip_progress", st.Progress, band.Time, Bar(st.Progress, band.Time)));
                if (paused) builder.AddLine(L("fc3_tip_paused"));
            }
            catch (Exception e)
            {
                log.Warning("tooltip failed: " + e.Message);
            }
        }

        static string Bar(int value, int target, int length = 12)
        {
            int filled = target <= 0 ? 0 : Math.Min(length, value * length / target);
            var sb = new StringBuilder("[");
            sb.Append('#', filled).Append('-', length - filled).Append(']');
            return sb.ToString();
        }

        static string L(string key, params object[] args)
            => ModHelper.GetLocalized(GameApi.ModId, key, args);
    }
}
