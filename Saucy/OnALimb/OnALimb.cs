using System;
using System.Diagnostics;
using System.Linq;

using ClickLib.Clicks;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.GeneratedSheets;

using Saucy.CuffACur;
using Saucy.TripleTriad;

namespace Saucy.OnALimb
{
    public unsafe class LimbModule
    {
        private static bool _enabled = false;
        public static bool ModuleEnabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (_enabled)
                {
                    Svc.Chat.ChatMessage += onChatMessage;
                }
                else
                {
                    Svc.Chat.ChatMessage -= onChatMessage;
                }
            }
        }
        public static bool PlayXTimes = false;
        public static int NumberOfTimes = 0;

        public delegate nint UnknownFunction(nint a1, ushort a2, int a3, void* a4);
        public static Hook<UnknownFunction> FuncHook;

        /// <summary>The name of the Out on a Limb minigame addon</summary>
        const string GAME_ADDON_NAME = "MiniGameBotanist";
        /// <summary>The difficulty selector addon</summary>
        const string DIFFICULTY_ADDON_NAME = "MiniGameAimg";
        const int MACHINE_ID = 2005423;
        /// <summary>The XivChatType for swing results, which isnt in the enum apparently.</summary>
        const XivChatType RESULT_CHAT_TYPE = (XivChatType)2105;
        const XivChatType MGP_CHAT_TYPE = (XivChatType)2238;

        private static int doubleDownCount = 0;
        private static float ROTATION_MAX = 0.73186f;   // observed value was 0.73186547
        private static float ROTATION_MIN = -0.73303f;  // observed value was -0.7330383

        private static readonly string miss = Svc.Data.GetExcelSheet<Addon>()?.GetRow((uint)HitType.Miss)?.Text.ToString();
        private static readonly string close = Svc.Data.GetExcelSheet<Addon>()?.GetRow((uint)HitType.Close)?.Text.ToString();
        private static readonly string veryClose = Svc.Data.GetExcelSheet<Addon>()?.GetRow((uint)HitType.VeryClose)?.Text.ToString();
        private static readonly string onTop = Svc.Data.GetExcelSheet<Addon>()?.GetRow((uint)HitType.OnTop)?.Text.ToString();

        private static PlayState? playState = null;
        // Chat messages with MGP results are a little delayed, so we're using this state to note
        // that a game has finished but the results have not been saved.
        private static bool _awaitingSave = false;
        private static bool awaitingSave
        {
            get => _awaitingSave;
            set
            {
                if (value)
                {
                    awaitingSince.Restart();
                }
                else
                {
                    //awaitingSince.Stop();
                    awaitingSince.Reset();
                }
                _awaitingSave = value;
            }
        }
        private static Stopwatch awaitingSince = new();

        private static readonly IChatGui.OnMessageDelegate onChatMessage =
            (XivChatType type, uint senderId, ref SeString sender, ref SeString message, ref bool isHandled) =>
        {
            switch (type)
            {
                case MGP_CHAT_TYPE:
                    awaitingSave = true;
                    var resultParts = message.TextValue.ToString().Split(' ');
                    var quantity = resultParts[2];

                    var numMGP = 0;
                    int.TryParse(quantity, out numMGP);

                    SaveStats(numMGP, playState?.timeRemaining ?? 0);

                    break;
                case RESULT_CHAT_TYPE:
                    if (playState != null)
                    {
                        if (message.TextValue.Equals(miss))
                            playState.Hit(HitType.Miss);
                        else if (message.TextValue.Equals(close))
                            playState.Hit(HitType.Close);
                        else if (message.TextValue.Equals(veryClose))
                            playState.Hit(HitType.VeryClose);
                        else if (message.TextValue.Equals(onTop))
                            playState.Hit(HitType.OnTop);
                    }
                    break;
            }
        };

        public static nint FuncDetour(nint a1, ushort a2, int a3, void* a4)
        {
            return FuncHook.Original(a1, a2, a3, a4);
        }

        public unsafe static void RunModule()
        {
            try
            {
                if (StartMachine()) return;

                if (StartGame()) return;

                if (SelectDifficulty()) return;

                if (DoubleDown()) return;

                if (PlayGame()) return;
            }
            catch (Exception e)
            {
                Svc.Log.Error(e, "OnALimb");
            }
        }

        /// <summary>
        /// Click a button within a visible addon.
        /// </summary>
        /// <param name="addon"></param>
        /// <param name="button"></param>
        private static void ClickButton(nint addon, AtkResNode* button)
        {
            var evt = stackalloc AtkEvent[]
            {
                new()
                {
                    Node = button,
                    Target = (AtkEventTarget*)button,
                    Param = 0,
                    NextEvent = null,
                    Type = (AtkEventType)0x17,
                    Unk29 = 0,
                    Flags = 0xDD
                }
            };

            FuncHook ??= Svc.Hook.HookFromAddress<UnknownFunction>(Svc.SigScanner.ScanText("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 30 0F B7 FA"), FuncDetour);
            FuncHook.Original((nint)addon, 0x17, 0, evt);
        }

        /// <summary>
        /// Click the yes confirmation to double down.
        /// </summary>
        /// <returns>True if the confirmation dialog was interacted with.</returns>
        private static bool DoubleDown()
        {
            if (!(ECommons.GenericHelpers.TryGetAddonByName<AddonSelectString>("SelectYesno", out var startMenu) && startMenu->AtkUnitBase.IsVisible))
                return false;

            try
            {
                if (doubleDownCount < Saucy.Config.LimbDoubleDownCount && playState?.timeRemaining >= Saucy.Config.LimbMinTime)
                {
                    ClickSelectYesNo.Using((IntPtr)startMenu).Yes();
                    doubleDownCount += 1;
                    playState = null;
                }
                else
                {
                    awaitingSave = true;
                    ClickSelectYesNo.Using((IntPtr)startMenu).No();
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool PlayGame()
        {
            var addon = Svc.GameGui.GetAddonByName(GAME_ADDON_NAME, 1);
            if (addon == IntPtr.Zero) return false;

            var ui = (AtkUnitBase*)addon;
            if (ui->IsVisible)
            {
                // node 9 - button component
                var button = ui->UldManager.NodeList[9];
                // node 17 - cursor res node
                // node 18 - cursor img node
                var cursor = ui->UldManager.NodeList[18];

                // max and min should be constant, but it wont hurt to update our values
                if (cursor->Rotation > LimbModule.ROTATION_MAX)
                {
                    LimbModule.ROTATION_MAX = cursor->Rotation;
                }
                else if (cursor->Rotation < LimbModule.ROTATION_MIN)
                {
                    LimbModule.ROTATION_MIN = cursor->Rotation;
                }

                playState ??= new();
                if (playState.complete)
                    return false;

                var timer = (AtkTextNode*)ui->UldManager.NodeList[21];
                if (timer != null)
                {
                    var time = timer->NodeText.ToString().Split(':')[^1];
                    if (uint.TryParse(time, out uint secondsLeft))
                    {
                        playState.timeRemaining = secondsLeft;
                    }
                }

                (float min, float max) = playState.Target;
                if (cursor->Rotation >= min && cursor->Rotation <= max)
                {
                    ClickButton(addon, button);
                    playState.lastLocation = cursor->Rotation;

                    return true;
                }
            }

            return false;
        }

        private static void SaveStats(int numMGP, uint timeRemaining)
        {
            awaitingSave = false;
            Saucy.Config.UpdateStats(stats =>
            {
                stats.LimbMGP += numMGP;
                stats.LimbGamesPlayed += 1;
                stats.LimbTime += 60 - timeRemaining;

                if (numMGP > 0)
                {
                    stats.LimbGamesWon += 1;

                    switch (numMGP)
                    {
                        case < 175:
                            stats.LimbLevel0 += 1;
                            break;
                        case < 260:
                            stats.LimbLevel1 += 1;
                            break;
                        case < 700:
                            stats.LimbLevel2 += 1;
                            break;
                        case < 875:
                            stats.LimbLevel3 += 1;
                            break;
                        case < 1050:
                            stats.LimbLevel4 += 1;
                            break;
                        case >= 1050:
                            stats.LimbLevel5 += 1;
                            break;
                    }
                }
            });

            playState = null;
            Saucy.Config.Save();

            if (PlayXTimes)
            {
                NumberOfTimes -= 1;

                if (NumberOfTimes == 0)
                {
                    NumberOfTimes = 1;
                    ModuleEnabled = false;

                    if (Saucy.Config.PlaySound)
                        Saucy.PlaySound();

                    if (TriadAutomater.LogOutAfterCompletion)
                    {
                        Svc.Framework.RunOnTick(() => TriadAutomater.Logout(), TimeSpan.FromMilliseconds(2000))
                            .ContinueWith((_) =>
                                Svc.Framework.RunOnTick(() => TriadAutomater.SelectYesLogout(), TimeSpan.FromMilliseconds(3500))
                            );
                    }
                }
            }
        }

        /// <summary>
        /// Select a difficulty level for the game by clicking when the indicator is in the appropriate colored zone.
        /// </summary>
        /// <returns>True if the difficulty selector was interacted with.</returns>
        private static bool SelectDifficulty()
        {
            var addon = Svc.GameGui.GetAddonByName(DIFFICULTY_ADDON_NAME, 1);
            if (addon == IntPtr.Zero) return false;

            var ui = (AtkUnitBase*)addon;
            if (ui->IsVisible)
            {
                var redRes = ui->UldManager.NodeList[17];
                var redImg = ui->UldManager.NodeList[18];

                // the visual indicator of our difficulty selection
                var selector = ui->UldManager.NodeList[19];
                var button = ui->UldManager.NodeList[20];

                var red = (redRes->Y + 2, redRes->Y + redImg->Height - 2);

                (float min, float max) difficulty = red;
                if (selector->Y >= difficulty.min && selector->Y <= difficulty.max)
                {
                    ClickButton(addon, button);

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Click the yes confirmation to start playing the game.
        /// </summary>
        /// <returns>True if the confirmation dialog was interacted with.</returns>
        private static bool StartGame()
        {
            if (!(ECommons.GenericHelpers.TryGetAddonByName<AddonSelectString>("SelectString", out var startDialog) && startDialog->AtkUnitBase.IsVisible))
                return false;

            try
            {
                ClickSelectString.Using((IntPtr)startDialog).SelectItem1();
                doubleDownCount = 0;
                playState = null;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Interact with the game machine.
        /// </summary>
        /// <returns>True if the machine was interacted with.</returns>
        private static bool StartMachine()
        {
            if (Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.OccupiedInQuestEvent])
                return false;

            if (awaitingSave)
            {
                if (awaitingSince.ElapsedMilliseconds >= 750)
                    SaveStats(0, 0);

                return true;
            }

            GameObject* limbMachine = (GameObject*)Svc.Objects
                        .Select(x => new { x.DataId, x.Address, Distance = CufModule.GetTargetDistance(x) })
                        .Where(x => x.DataId == MACHINE_ID && x.Distance <= 2f)
                        .OrderByDescending(x => x.Distance)
                        .FirstOrDefault()?.Address;
            if ((IntPtr)limbMachine == IntPtr.Zero)
                return false;

            TargetSystem* targetSystem = TargetSystem.Instance();
            targetSystem->InteractWithObject(limbMachine);

            return true;
        }

        internal class PlayState
        {
            public HitType? lastType = null;
            public float? lastLocation = null;
            public uint? timeRemaining = null;
            public bool initial = true;
            public bool complete = false;
            public const float OFFSET_MIN = 0.0366f;

            public (float min, float max) Target
                    => (isLow)
                            ? (LowBound - OFFSET_MIN, LowBound + OFFSET_MIN)
                            : (HighBound - OFFSET_MIN, HighBound + OFFSET_MIN);
            private float shrink = 0.25f * (ROTATION_MAX - ROTATION_MIN);
            private bool isLow = true;

            private float _lowBound = ROTATION_MIN;
            private float LowBound
            {
                get => _lowBound;
                set
                {
                    _lowBound = MathF.Max(ROTATION_MIN, value);
                }
            }

            private float _highBound = ROTATION_MAX;
            private float HighBound
            {
                get => _highBound;
                set
                {
                    _highBound = MathF.Min(ROTATION_MAX, value);
                }
            }

            public void Hit(HitType hitType)
            {
                if (hitType == HitType.OnTop)
                {
                    OnDirectHit();
                    return;
                }

                if (hitType == HitType.Miss)
                {
                    OnMiss();
                    return;
                }

                initial = false;

                if (lastLocation == null) return;

                if (hitType == HitType.Close)
                {
                    OnClose();
                }

                if (hitType == HitType.VeryClose)
                {
                    OnVeryClose();
                }
            }

            private void OnClose()
            {
                switch (lastType)
                {
                    case HitType.Miss:
                        shrink = 0.125f * (ROTATION_MAX - ROTATION_MIN);
                        Recenter((float)lastLocation);
                        break;

                    case HitType.VeryClose:
                        if (isLow)
                        {
                            LowBound = (float)lastLocation;
                        }
                        else
                        {
                            HighBound = (float)lastLocation;
                        }
                        break;

                    default:
                        if (isLow)
                        {
                            LowBound += shrink;
                        }
                        else
                        {
                            HighBound -= shrink;
                        }
                        break;
                }

                lastType = HitType.Close;
                NextTarget();
            }

            private void OnMiss()
            {
                if (isLow)
                {
                    LowBound += shrink;
                }
                else
                {
                    HighBound -= shrink;
                }

                lastType = HitType.Miss;
                NextTarget();
            }

            private void OnDirectHit()
            {
                complete = true;
                lastType = HitType.OnTop;
            }

            private void OnVeryClose()
            {
                if (lastType != HitType.VeryClose)
                {
                    shrink = 0.0625f * (ROTATION_MAX - ROTATION_MIN);
                    Recenter((float)lastLocation);
                }
                else
                {
                    if (isLow)
                    {
                        LowBound = (float)lastLocation - shrink;
                    }
                    else
                    {
                        HighBound = (float)lastLocation + shrink;
                    }
                    NextTarget();
                }

                lastType = HitType.VeryClose;
            }

            private void NextTarget()
            {
                isLow = !isLow;
            }

            private void Recenter(float c)
            {
                LowBound = c - shrink;
                HighBound = c + shrink;
            }
        }
    }

    public enum HitType
    {
        Miss = 9706,
        Close = 9707,
        VeryClose = 9708,
        OnTop = 9709,
    }
}
