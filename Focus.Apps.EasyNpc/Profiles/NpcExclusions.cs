using Focus.Apps.EasyNpc.Configuration;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Subjects;

namespace Focus.Apps.EasyNpc.Profiles
{
    // NPCs the user wants left out of the merge entirely: typically everything a mod like Madmen or Bandit War adds, or
    // a handful of NPCs another tool (NPC Plugin Chooser, a SkyPatcher replacer) is meant to handle. Kept outside the
    // profile's event log on purpose, so it can't disturb how saved face choices are replayed.
    public interface INpcExclusions
    {
        int Count { get; }
        // The current set of excluded keys; emits again whenever it changes.
        IObservable<IReadOnlySet<IRecordKey>> Excluded { get; }

        void Clear();
        int Exclude(IEnumerable<IRecordKey> keys);
        int Include(IEnumerable<IRecordKey> keys);
        bool IsExcluded(IRecordKey key);
    }

    public class NpcExclusions : INpcExclusions
    {
        public static readonly string DefaultPath = Path.Combine(ProgramData.DirectoryPath, "Exclusions.json");

        public int Count
        {
            get
            {
                lock (keys)
                    return keys.Count;
            }
        }

        public IObservable<IReadOnlySet<IRecordKey>> Excluded => excluded;

        private readonly BehaviorSubject<IReadOnlySet<IRecordKey>> excluded;
        private readonly HashSet<IRecordKey> keys = new(RecordKeyComparer.Default);
        private readonly ILogger log;
        private readonly string path;

        public NpcExclusions(ILogger log) : this(DefaultPath, log) { }

        public NpcExclusions(string path, ILogger log)
        {
            this.log = log;
            this.path = path;
            Load();
            excluded = new(Snapshot());
        }

        public void Clear()
        {
            lock (keys)
                keys.Clear();
            Commit();
        }

        public int Exclude(IEnumerable<IRecordKey> keysToExclude)
        {
            int changed;
            lock (keys)
                changed = keysToExclude.Count(k => keys.Add(new RecordKey(k)));
            if (changed > 0)
                Commit();
            return changed;
        }

        public int Include(IEnumerable<IRecordKey> keysToInclude)
        {
            int changed;
            lock (keys)
                changed = keysToInclude.Count(k => keys.Remove(k));
            if (changed > 0)
                Commit();
            return changed;
        }

        public bool IsExcluded(IRecordKey key)
        {
            lock (keys)
                return keys.Contains(key);
        }

        private void Commit()
        {
            Save();
            excluded.OnNext(Snapshot());
        }

        private void Load()
        {
            if (!File.Exists(path))
                return;
            try
            {
                var saved = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? new();
                foreach (var value in saved)
                {
                    try
                    {
                        keys.Add(RecordKey.Parse(value));
                    }
                    catch (ArgumentException)
                    {
                        // One bad entry shouldn't cost the others.
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                log.Warning(ex, "Couldn't read the NPC exclusion list at {path}; starting with none.", path);
            }
        }

        private void Save()
        {
            try
            {
                List<string> values;
                lock (keys)
                    values = keys.Select(k => new RecordKey(k).ToString()).OrderBy(x => x).ToList();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonConvert.SerializeObject(values, Formatting.Indented));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Error(ex, "Couldn't save the NPC exclusion list to {path}.", path);
            }
        }

        private IReadOnlySet<IRecordKey> Snapshot()
        {
            lock (keys)
                return new HashSet<IRecordKey>(keys, RecordKeyComparer.Default);
        }
    }
}
