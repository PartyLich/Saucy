using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace Saucy
{
    using Newtonsoft.Json;

    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public bool UseRecommendedDeck { get; set; } = false;

        public int SelectedDeckIndex { get; set; } = -1;

        public Stats Stats { get; set; } = new Stats();

        [JsonIgnore]
        public Stats SessionStats { get; set; } = new Stats();

        public bool PlaySound { get; set; } = false;
        public string SelectedSound { get; set; } = "Moogle";
        public bool OnlyUnobtainedCards { get; set; } = false;
        public bool OpenAutomatically { get; set; } = false;

        public bool SliceIsRightModuleEnabled { get; set; }

        private int _limbDoubleDownCount = 3;
        /// <summary>Maximum double down attempts.</summary>
        public int LimbDoubleDownCount
        {
            get => _limbDoubleDownCount;
            set
            {
                _limbDoubleDownCount = Math.Min(5, value);
            }
        }

        private uint _limbMinTime = 15;
        /// <summary>Minimum time remaining when selecting double down.</summary>
        public uint LimbMinTime
        {
            get => _limbMinTime;
            set
            {
                _limbMinTime = Math.Min(60, value);
            }
        }

        // the below exist just to make saving less cumbersome

        [NonSerialized]
        private DalamudPluginInterface? pluginInterface;

        public void UpdateStats(Action<Stats> updateAction)
        {
            updateAction(Stats);
            updateAction(SessionStats);
        }

        public void Initialize(DalamudPluginInterface pluginInterface)
        {
            this.pluginInterface = pluginInterface;
        }

        public void Save()
        {
            pluginInterface!.SavePluginConfig(this);
        }
    }
}
