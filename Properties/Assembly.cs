using MelonLoader;

[assembly: MelonInfo(typeof(AutoResumeSurvival.AutoResumeMod), "AutoResumeSurvival", "1.0.0", "leatherneck6017")]
[assembly: MelonGame("Hinterland", "TheLongDark")]

namespace AutoResumeSurvival
{
    public class Main : MelonMod
    {
        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("AutoResumeSurvival initialized.");
        }
    }
}