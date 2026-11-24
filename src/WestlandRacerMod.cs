using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(WestlandRacerMod.WestlandRacerMod), "Wasteland Racer AI", "1.0.0", "AlirezaFeStiCal")]
[assembly: MelonGame(null, null)]

namespace WestlandRacerMod
{
    /// <summary>
    /// Entry point.
    ///
    /// The mod is deliberately passive about game rules: it reads the same
    /// caches the game's own panels read and sends the same public requests the
    /// UI buttons send (Match / SelectCar / UseCard / UseProp / Shuffle). It
    /// never forges packets, never edits configs, and never touches the
    /// competitive formulae - the server stays authoritative. Every step that
    /// can fail is wrapped so the worst outcome is a skipped action.
    ///
    /// Press F9 in game to show/hide the overlay.
    /// </summary>
    public sealed class WestlandRacerMod : MelonMod
    {
        private readonly RacerSettings _settings = new RacerSettings();
        private readonly RacerBrain _brain;
        private readonly RacerUi _ui = new RacerUi();

        public WestlandRacerMod()
        {
            _brain = new RacerBrain(_settings);
        }

        public override void OnInitializeMelon()
        {
            Log.Info("Wasteland Racer AI v1.1.0 loaded.");
            Log.Info("Press F9 in game to show/hide the panel. Master switch defaults to ON.");
            Log.Info("'Auto match' (auto-enter races) defaults to OFF - enable it in the F9 panel if you want the mod to enter races by itself. " +
                     "Each auto-started match consumes one Racer Pass (item 48515), just like pressing Start Match by hand.");

            try
            {
                ReplyPatches.Apply(_brain);
            }
            catch (System.Exception ex)
            {
                Log.Warn("Could not install the reply observer patches: " + ex.Message);
            }
        }

        public override void OnUpdate()
        {
            try
            {
                if (Input.GetKeyDown(_ui.ToggleKey))
                    _ui.Visible = !_ui.Visible;

                _brain.Tick(Time.deltaTime);
            }
            catch (System.Exception ex)
            {
                // This runs every frame; keep it to one line per unique failure.
                Log.WarnOnce("OnUpdate: " + ex.Message);
            }
        }

        public override void OnGUI()
        {
            try
            {
                _ui.Draw(_settings, _brain);
            }
            catch (System.Exception ex)
            {
                Log.WarnOnce("OnGUI: " + ex.Message);
            }
        }

        public override void OnDeinitializeMelon()
        {
            _settings.Enabled = false;
            Log.Info("Wasteland Racer AI unloaded.");
        }
    }
}
