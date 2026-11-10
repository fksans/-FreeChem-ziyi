using System;

namespace FreeChem
{
    /// <summary>
    /// 仪器 / 加热器 / 耗材的「当前状态」提示行（问题 5c：让仪器可用、状态可见）。
    ///
    /// 只做「读取状态 + 拼本地化文案」，不持有任何状态：
    ///  · 全局档位来自 ChemRuntime（Temp / Ph / Balanced / Protected）；
    ///  · 单件状态来自物品 tag（TFuel 灯内燃料 ml、TWickDur 灯芯耐久、TFilterDur 滤纸剩余次数）。
    ///
    /// 本地化键（由 docs-i18n 提供）：
    ///  inst_temp / inst_ph / inst_balance / inst_protection / inst_fuel / inst_wick / inst_filter，
    ///  state_on / state_off，temp_0..temp_3。
    /// </summary>
    public static class ChemEquipment
    {
        /// <summary>返回该物品的状态提示行；不需要/不识别时返回空串（空串不显示）。</summary>
        public static string StatusLine(GameItem item)
        {
            if (item == null) return "";
            string id = item.identifier;
            try
            {
                switch (id)
                {
                    case "thermometer":
                        // 全局实验温度档：0 冷 / 1 常温 / 2 加热 / 3 高温高压
                        return L("inst_temp", ChemRuntime.Temp, L("temp_" + ChemRuntime.Temp));
                    case "ph_meter":
                        return L("inst_ph", ChemRuntime.Ph);
                    case "precision_balance":
                        return L("inst_balance", L(ChemRuntime.Balanced ? "state_on" : "state_off"));
                    case "safety_goggles":
                        return L("inst_protection", L(ChemRuntime.Protected ? "state_on" : "state_off"));
                    case "alcohol_lamp":
                    case "spirit_burner":
                        return L("inst_fuel", ChemCatalog.ReadInt(item, ChemCatalog.TFuel, 0));
                    case "wick_cotton":
                    case "wick_ceramic":
                        return L("inst_wick", ChemCatalog.ReadInt(item, ChemCatalog.TWickDur, 0));
                    case "filter_paper":
                    case "filter_paper_fine":
                    case "filter_paper_ash":
                        return L("inst_filter", ChemCatalog.ReadInt(item, ChemCatalog.TFilterDur, 0));
                    default:
                        return "";
                }
            }
            catch (Exception e)
            {
                ChemRuntime.Log("[equipment] status line failed for " + id + ": " + e.Message);
                return "";
            }
        }

        static string L(string key, params object[] args)
            => ModHelper.GetLocalized(ChemCatalog.ModId, key, args);
    }
}
