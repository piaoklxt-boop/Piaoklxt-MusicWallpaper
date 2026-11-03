using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace QQMusicWallpaper;

internal enum InteractionOutcome
{
	Routed, Delivered, Presented, Closed, Cancelled, SourceChanged, TargetMissing,
	AmbiguousTarget, DispatchFailed, PopupTimeout, InternalFailure, RecoveryRequested
}

// An allowlisted schema, not a scrubbed copy of the application log. No method
// accepts window text, paths, account data, exception messages or screenshots.
internal sealed class InteractionDiagnostics
{
	private sealed class Entry
	{
		public long Click, Generation, Started, Age;
		public DesktopClickRoute Route;
		public DesktopSpecialAction Action;
		public InteractionOutcome Outcome;
		public long Elapsed;
	}
	private readonly object sync = new object();
	private readonly List<Entry> entries = new List<Entry>(32);
	private readonly Func<long> milliseconds;
	private long nextNotice;
	private InteractionOutcome? pendingNotice;
	public InteractionDiagnostics() : this(() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency) { }
	internal InteractionDiagnostics(Func<long> milliseconds) { this.milliseconds = milliseconds; }

	public void Begin(long click, DesktopClickRoute route, DesktopSpecialAction action, long age)
	{
		lock (sync)
		{
			Entry existing = Find(click);
			if (existing != null)
			{
				if (existing.Action == DesktopSpecialAction.None && action != DesktopSpecialAction.None)
				{ existing.Action = action; existing.Route = route; }
				return;
			}
			if (entries.Count == 32) entries.RemoveAt(0);
			entries.Add(new Entry { Click = click, Route = route, Action = action,
				Age = age, Started = milliseconds(), Outcome = InteractionOutcome.Routed });
		}
	}

	public void Complete(long click, long generation, InteractionOutcome outcome)
	{
		lock (sync)
		{
			Entry entry = Find(click);
			if (entry == null) return; // A late worker cannot evict more recent clicks.
			entry.Generation = generation;
			entry.Outcome = outcome;
			entry.Elapsed = Math.Max(0, milliseconds() - entry.Started);
			int failures = 0;
			for (int i = entries.Count - 1; i >= 0 && IsFailure(entries[i].Outcome); i--) failures++;
			if (failures >= 3 && milliseconds() >= nextNotice)
			{
				pendingNotice = outcome;
				nextNotice = milliseconds() + 60000;
			}
			else if (entries.Count > 0 && !IsFailure(entries[entries.Count - 1].Outcome))
				pendingNotice = null;
		}
	}

	public bool TakeNotice(out InteractionOutcome reason)
	{
		lock (sync)
		{
			reason = pendingNotice ?? InteractionOutcome.Routed;
			bool available = pendingNotice.HasValue;
			pendingNotice = null;
			return available;
		}
	}

	public string Export()
	{
		lock (sync)
		{
			var result = new StringBuilder("MusicWallpaper interaction diagnostics v1\n");
			result.Append("Application=").Append(AppInfo.Version).Append('\n');
			result.Append("Scope=last 32 routed desktop interactions; local only\n");
			result.Append("click\taction\troute\trequestGeneration\tsnapshotAgeMs\tresult\telapsedMs\n");
			foreach (Entry e in entries)
			{
				result.Append(e.Click.ToString(CultureInfo.InvariantCulture)).Append('\t')
					.Append(e.Action).Append('\t').Append(e.Route).Append('\t')
					.Append(e.Generation.ToString(CultureInfo.InvariantCulture)).Append('\t')
					.Append(e.Age.ToString(CultureInfo.InvariantCulture)).Append('\t')
					.Append(e.Outcome).Append('\t').Append(e.Elapsed.ToString(CultureInfo.InvariantCulture)).Append('\n');
			}
			return result.ToString();
		}
	}

	private Entry Find(long click)
	{
		for (int i = entries.Count - 1; i >= 0; i--)
			if (entries[i].Click == click) return entries[i];
		return null;
	}
	private static bool IsFailure(InteractionOutcome value)
	{
		return value == InteractionOutcome.TargetMissing || value == InteractionOutcome.AmbiguousTarget ||
			value == InteractionOutcome.DispatchFailed || value == InteractionOutcome.PopupTimeout ||
			value == InteractionOutcome.InternalFailure;
	}
}
