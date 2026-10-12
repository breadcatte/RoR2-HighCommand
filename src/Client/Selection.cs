using System;
using System.Collections.Generic;
using HighCommand.Army;
using UnityEngine;

namespace HighCommand.Client
{
    /// <summary>
    /// The commander's current selection and control groups. Holds Hero objects; heroes that stop existing are
    /// dropped lazily. The selection is purely local (each commander has their own).
    /// </summary>
    internal sealed class Selection
    {
        internal const int GroupCount = 10;

        private readonly List<Hero> selected = new List<Hero>();
        private readonly List<Hero>[] groups = new List<Hero>[GroupCount];
        private readonly float[] groupLastRecall = new float[GroupCount];

        /// <summary>Raised after any change of the selection.</summary>
        internal event Action Changed;

        internal Selection()
        {
            for (int i = 0; i < GroupCount; i++) groups[i] = new List<Hero>();
        }

        internal IReadOnlyList<Hero> Heroes
        {
            get
            {
                Prune();
                return selected;
            }
        }

        internal int Count
        {
            get
            {
                Prune();
                return selected.Count;
            }
        }

        internal bool Contains(Hero hero) => hero != null && selected.Contains(hero);

        internal void Clear()
        {
            if (selected.Count == 0) return;
            selected.Clear();
            Changed?.Invoke();
        }

        internal void Set(IEnumerable<Hero> heroes)
        {
            selected.Clear();
            foreach (Hero h in heroes) if (h != null && h.Exists && !selected.Contains(h)) selected.Add(h);
            Changed?.Invoke();
        }

        internal void SetSingle(Hero hero)
        {
            selected.Clear();
            if (hero != null && hero.Exists) selected.Add(hero);
            Changed?.Invoke();
        }

        internal void Add(IEnumerable<Hero> heroes)
        {
            bool any = false;
            foreach (Hero h in heroes)
            {
                if (h == null || !h.Exists || selected.Contains(h)) continue;
                selected.Add(h);
                any = true;
            }
            if (any) Changed?.Invoke();
        }

        internal void Toggle(Hero hero)
        {
            if (hero == null || !hero.Exists) return;
            if (!selected.Remove(hero)) selected.Add(hero);
            Changed?.Invoke();
        }

        // ---- control groups ----------------------------------------------------------------------

        internal void AssignGroup(int index)
        {
            if (index < 0 || index >= GroupCount) return;
            Prune();
            groups[index].Clear();
            groups[index].AddRange(selected);
        }

        internal void AddToGroup(int index)
        {
            if (index < 0 || index >= GroupCount) return;
            Prune();
            foreach (Hero h in selected) if (!groups[index].Contains(h)) groups[index].Add(h);
        }

        /// <summary>Selects a group. Returns true when it was pressed twice quickly (the caller flies the camera there).</summary>
        internal bool RecallGroup(int index, float now)
        {
            if (index < 0 || index >= GroupCount) return false;
            groups[index].RemoveAll(h => h == null || !h.Exists);
            if (groups[index].Count == 0) return false;

            bool doubleTap = now - groupLastRecall[index] < 0.35f;
            groupLastRecall[index] = now;
            Set(groups[index]);
            return doubleTap;
        }

        internal IReadOnlyList<Hero> Group(int index) => groups[index];

        /// <summary>Forget everything (new run).</summary>
        internal void Reset()
        {
            selected.Clear();
            for (int i = 0; i < GroupCount; i++) groups[i].Clear();
            Changed?.Invoke();
        }

        private void Prune()
        {
            int removed = selected.RemoveAll(h => h == null || !h.Exists);
            if (removed > 0) Changed?.Invoke();
        }

        /// <summary>Average position of the selected heroes (or null).</summary>
        internal Vector3? Center()
        {
            Prune();
            if (selected.Count == 0) return null;
            Vector3 sum = Vector3.zero;
            foreach (Hero h in selected) sum += h.Position;
            return sum / selected.Count;
        }
    }
}
