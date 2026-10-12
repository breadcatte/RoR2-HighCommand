using System.Collections.Generic;
using HighCommand.Army;
using HighCommand.UI;
using RoR2;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// Tells the commander what the army is not looking at: a hero that falls, or one that is nearly dead. Purely
    /// local: it only watches the heroes' replicated state, so it works the same on a host and on a client. The place
    /// of the last alert can be flown to with a key.
    /// </summary>
    internal sealed class AlertService
    {
        private const float LowHealth = 0.22f;      // fraction at which a hero counts as nearly dead
        private const float Rearm = 0.45f;           // it must recover above this before it can alarm again
        private const float MinGap = 4f;             // seconds between two alerts about the same hero

        private sealed class State
        {
            internal bool wasAlive = true;
            internal bool low;
            internal float lastAlert = -100f;
        }

        private readonly Dictionary<int, State> states = new Dictionary<int, State>();
        private readonly List<int> stale = new List<int>();

        /// <summary>Where the most recent alert happened (valid when <see cref="HasAlert"/>).</summary>
        internal Vector3 LastPosition { get; private set; }

        internal bool HasAlert { get; private set; }

        internal float LastTime { get; private set; } = -100f;

        internal void Reset()
        {
            states.Clear();
            HasAlert = false;
        }

        internal void Tick(IReadOnlyList<Hero> heroes)
        {
            if (!Settings.Alerts.Value) return;
            float now = Time.unscaledTime;

            for (int i = 0; i < heroes.Count; i++)
            {
                Hero hero = heroes[i];
                if (!states.TryGetValue(hero.Id, out State s))
                {
                    s = new State();
                    states[hero.Id] = s;
                }

                CharacterBody body = hero.Body;
                bool alive = hero.IsAlive;

                if (s.wasAlive && !alive && body)
                {
                    Raise(hero, s, now, died: true, position: body.corePosition);
                }
                else if (alive && body && body.healthComponent)
                {
                    HealthComponent hc = body.healthComponent;
                    float total = Mathf.Max(1f, hc.fullCombinedHealth);
                    float fraction = hc.combinedHealth / total;
                    if (!s.low && fraction < LowHealth && now - s.lastAlert > MinGap)
                    {
                        s.low = true;
                        Raise(hero, s, now, died: false, position: body.corePosition);
                    }
                    else if (s.low && fraction > Rearm) s.low = false;
                }
                s.wasAlive = alive || !body; // a hero without a body is between spawns, not freshly dead
            }

            // forget heroes that are gone
            if (states.Count > heroes.Count + 4)
            {
                stale.Clear();
                foreach (KeyValuePair<int, State> kv in states)
                {
                    bool found = false;
                    for (int i = 0; i < heroes.Count && !found; i++) found = heroes[i].Id == kv.Key;
                    if (!found) stale.Add(kv.Key);
                }
                foreach (int id in stale) states.Remove(id);
            }
        }

        private void Raise(Hero hero, State s, float now, bool died, Vector3 position)
        {
            s.lastAlert = now;
            LastPosition = position;
            LastTime = now;
            HasAlert = true;

            string name = hero.DisplayName;
            Plugin.Log.LogInfo($"Alert: {hero} {(died ? "has fallen" : "is nearly dead")} at {position:0}");
            Toast.Show(Loc.Format(died ? "HIGHCOMMAND_ALERT_DIED" : "HIGHCOMMAND_ALERT_LOWHP", name));
            Feedback.Ping(position, died ? new Color(1f, 0.15f, 0.15f, 1f) : new Color(1f, 0.6f, 0.1f, 1f), died ? 2.6f : 1.8f);
            if (died)
            {
                try { Util.PlaySound("Play_UI_insufficient_funds", RoR2Application.instance ? RoR2Application.instance.gameObject : null); }
                catch (System.Exception) { /* sounds are a nicety */ }
            }
        }
    }
}
