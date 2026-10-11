using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using RoR2;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HighCommand.Dev
{
    /// <summary>
    /// Developer script runner. If BepInEx\config\HighCommand.autoexec.txt exists, its lines are run once the game
    /// has finished loading. Lets the whole "start game, make a run, check, quit" cycle run without a human.
    ///
    /// Line syntax (one per line, # starts a comment):
    ///   some_console_command args      run a console command
    ///   @wait 5                        wait 5 real seconds
    ///   @scene lobby                   wait until the active scene has this name
    ///   @run                           wait until a Run exists
    ///   @norun                         wait until the Run is gone
    ///   @log text                      write text to the BepInEx log
    ///   @quit                          close the game
    /// </summary>
    internal class AutoExec : MonoBehaviour
    {
        private const string FileName = "HighCommand.autoexec.txt";

        internal static void Install()
        {
            string path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, FileName);
            if (!File.Exists(path)) return;

            var host = new GameObject("HighCommand.AutoExec");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<AutoExec>().scriptPath = path;
            Plugin.Log.LogInfo($"AutoExec: script found, will run after load: {path}");
        }

        private string scriptPath;

        private IEnumerator Start()
        {
            // Wait for the game itself to finish initialising (main menu is up).
            while (!RoR2Application.loadFinished) yield return null;
            yield return new WaitForSecondsRealtime(3f);

            string[] lines;
            try { lines = File.ReadAllLines(scriptPath); }
            catch (Exception e) { Plugin.Log.LogError("AutoExec: cannot read script: " + e.Message); yield break; }

            Plugin.Log.LogInfo($"AutoExec: running {lines.Length} lines");
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] != '@')
                {
                    Plugin.Log.LogInfo("AutoExec> " + line);
                    try { RoR2.Console.instance.SubmitCmd(null, line); }
                    catch (Exception e) { Plugin.Log.LogError("AutoExec: command failed: " + e); }
                    yield return null;
                    continue;
                }

                string[] parts = line.Split(new[] { ' ' }, 2);
                string verb = parts[0].ToLowerInvariant();
                string arg = parts.Length > 1 ? parts[1].Trim() : "";
                switch (verb)
                {
                    case "@wait":
                        float seconds = 1f;
                        float.TryParse(arg, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
                        yield return new WaitForSecondsRealtime(seconds);
                        break;
                    case "@scene":
                        {
                            float timeout = 120f;
                            while (SceneManager.GetActiveScene().name != arg && timeout > 0f)
                            {
                                timeout -= Time.unscaledDeltaTime;
                                yield return null;
                            }
                            Plugin.Log.LogInfo($"AutoExec: scene '{SceneManager.GetActiveScene().name}' (wanted '{arg}')");
                            yield return new WaitForSecondsRealtime(1.5f);
                            break;
                        }
                    case "@run":
                        {
                            float timeout = 120f;
                            while (!Run.instance && timeout > 0f)
                            {
                                timeout -= Time.unscaledDeltaTime;
                                yield return null;
                            }
                            Plugin.Log.LogInfo("AutoExec: run " + (Run.instance ? "exists: " + Run.instance.name : "did not appear"));
                            break;
                        }
                    case "@norun":
                        {
                            float timeout = 120f;
                            while (Run.instance && timeout > 0f)
                            {
                                timeout -= Time.unscaledDeltaTime;
                                yield return null;
                            }
                            break;
                        }
                    case "@log":
                        Plugin.Log.LogInfo("AutoExec: " + arg);
                        break;
                    case "@quit":
                        Plugin.Log.LogInfo("AutoExec: quit");
                        Application.Quit();
                        yield break;
                    default:
                        Plugin.Log.LogWarning("AutoExec: unknown directive " + verb);
                        break;
                }
            }
            Plugin.Log.LogInfo("AutoExec: script finished");
        }
    }
}
