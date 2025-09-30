// ============================================================================
//  RegionTemplates.cs
//  ACESimBase.Util.ArrayProcessing.Templating
//  Author-time helpers for identical-action sets and repeated windows,
//  plus a general-purpose parameter frame (stable VS slots).
// ============================================================================

using System;
using System.Runtime.CompilerServices;

namespace ACESimBase.Util.ArrayProcessing.Templating
{
    /// <summary>Configuration for templating helpers.</summary>
    public sealed class RegionTemplateOptions
    {
        public bool IncludeComments { get; init; } = true;
        public bool ManageDepthScopes { get; init; } = false;
        public string ChunkNamePrefix { get; init; } = null;
    }

    // ========================================================================
    //  IdenticalRangeTemplate
    // ========================================================================

    /// <summary>
    /// Record the first action body once; replay subsequent actions 1:1 by span.
    /// </summary>
    public sealed class IdenticalRangeTemplate
    {
        private readonly ArrayCommandList _acl;
        private readonly RegionTemplateOptions _opts;

        private string _currentSetName;
        private int? _firstChunkStart;
        private int _actionCount;
        private bool _inSet;

        public IdenticalRangeTemplate(ArrayCommandList acl, RegionTemplateOptions options = null)
        {
            _acl = acl ?? throw new ArgumentNullException(nameof(acl));
            _opts = options ?? new RegionTemplateOptions();
        }

        /// <summary>Begin an identical set. Dispose to close the set.</summary>
        public IDisposable BeginSet(string setName)
        {
            if (_inSet)
                throw new InvalidOperationException("An identical set is already open.");

            _currentSetName = setName ?? "IdenticalSet";
            _firstChunkStart = null;
            _actionCount = 0;
            _inSet = true;

            if (_opts.IncludeComments)
                _acl.InsertComment($"[IDENTICAL-BEGIN] set={_currentSetName}");

            return new SetScope(this);
        }

        /// <summary>
        /// Begin an action body within the current set. Dispose to close the action chunk.
        /// The first action is recorded; subsequent actions replay by span.
        /// </summary>
        public IDisposable BeginAction(string actionLabel)
        {
            if (!_inSet)
                throw new InvalidOperationException("BeginSet must be called before BeginAction.");

            bool isFirst = _actionCount == 0;

            string chunkName = string.IsNullOrEmpty(_opts.ChunkNamePrefix)
                ? $"Identical:{_currentSetName}"
                : $"{_opts.ChunkNamePrefix}:Identical:{_currentSetName}";

            int? identicalStart = isFirst ? (int?)null : _firstChunkStart;
            _acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: identicalStart, name: chunkName);

            if (isFirst && _firstChunkStart == null)
                _firstChunkStart = _acl.NextCommandIndex;

            if (_opts.IncludeComments)
                _acl.InsertComment($"[IDENTICAL-ACTION] set={_currentSetName} action={actionLabel}");

            if (_opts.ManageDepthScopes)
                _acl.Recorder.IncrementDepth();

            _actionCount++;
            return new ActionScope(this, isFirst);
        }

        private sealed class SetScope : IDisposable
        {
            private readonly IdenticalRangeTemplate _t;
            private bool _disposed;

            public SetScope(IdenticalRangeTemplate t) => _t = t;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_t._opts.IncludeComments)
                    _t._acl.InsertComment($"[IDENTICAL-END] set={_t._currentSetName}");

                _t._inSet = false;
                _t._currentSetName = null;
                _t._firstChunkStart = null;
                _t._actionCount = 0;
            }
        }

        private sealed class ActionScope : IDisposable
        {
            private readonly IdenticalRangeTemplate _t;
            private readonly bool _isFirst;
            private bool _disposed;

            public ActionScope(IdenticalRangeTemplate t, bool isFirst)
            {
                _t = t;
                _isFirst = isFirst;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_t._opts.ManageDepthScopes)
                    _t._acl.Recorder.DecrementDepth();

                _t._acl.EndCommandChunk(endingRepeatedChunk: !_isFirst);
            }
        }
    }

    // ========================================================================
    //  RepeatWindowTemplate
    // ========================================================================

    /// <summary>
    /// Record the first window once; replay subsequent windows by span.
    /// </summary>
    public sealed class RepeatWindowTemplate
    {
        private readonly ArrayCommandList _acl;
        private readonly CommandRecorder _r;
        private readonly RegionTemplateOptions _opts;

        private int? _firstWindowStart;
        private bool _inWindow;
        private bool _replaying;
        private string _windowName;

        public RepeatWindowTemplate(ArrayCommandList acl, RegionTemplateOptions options = null)
        {
            _acl = acl ?? throw new ArgumentNullException(nameof(acl));
            _r = _acl.Recorder ?? throw new InvalidOperationException("ACL recorder not initialized.");
            _opts = options ?? new RegionTemplateOptions();
        }

        /// <summary>Open a window. Dispose to close; safe to nest with other scopes.</summary>
        public WindowScope Open(string windowName = "RepeatedWindow")
        {
            if (_inWindow)
                throw new InvalidOperationException("A repeated window is already open.");

            _windowName = windowName ?? "RepeatedWindow";

            string chunkName = string.IsNullOrEmpty(_opts.ChunkNamePrefix)
                ? _windowName
                : $"{_opts.ChunkNamePrefix}:{_windowName}";

            if (_firstWindowStart == null)
            {
                _acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: null, name: chunkName);
                _firstWindowStart = _acl.NextCommandIndex;
                _replaying = false;
            }
            else
            {
                _acl.StartCommandChunk(runChildrenInParallel: false, identicalStartCommandRange: _firstWindowStart, name: chunkName);
                _replaying = true;
            }

            if (_opts.IncludeComments)
                _acl.InsertComment($"[REPEAT-BEGIN] name={_windowName} replaying={_replaying} firstStart={_firstWindowStart} nextCI={_acl.NextCommandIndex}");

            if (_opts.ManageDepthScopes)
                _r.IncrementDepth();

            _inWindow = true;
            return new WindowScope(this);
        }

        /// <summary>Close the window at a boundary. Safe to call multiple times.</summary>
        public void CloseAtBoundary()
        {
            if (!_inWindow)
                return;

            if (_opts.IncludeComments)
                _acl.InsertComment($"[REPEAT-END   ] name={_windowName} nextCI={_acl.NextCommandIndex}");

            if (_opts.ManageDepthScopes)
                _r.DecrementDepth();

            _acl.EndCommandChunk(endingRepeatedChunk: _replaying);

            _inWindow = false;
            _replaying = false;
            _windowName = null;
        }

        internal void CloseIfOpen() => CloseAtBoundary();

        public readonly struct WindowScope : IDisposable
        {
            private readonly RepeatWindowTemplate _t;
            public WindowScope(RepeatWindowTemplate t) { _t = t; }
            public void Dispose() => _t.CloseIfOpen();
        }
    }

    // ========================================================================
    //  ParameterFrame
    // ========================================================================

    /// <summary>
    /// Stable VS slots for caller-provided “parameters”. Can be refreshed
    /// from VS indices or from original sources. Independent of any domain.
    /// </summary>
    public sealed class ParameterFrame
    {
        private readonly ArrayCommandList _acl;
        private readonly CommandRecorder _r;
        private readonly int[] _slots;

        public ParameterFrame(ArrayCommandList acl, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _acl = acl ?? throw new ArgumentNullException(nameof(acl));
            _r = _acl.Recorder ?? throw new InvalidOperationException("ACL recorder not initialized.");

            _slots = new int[count];
            for (int i = 0; i < count; i++)
                _slots[i] = _r.NewZero();
        }

        /// <summary>Number of parameter slots.</summary>
        public int Count => _slots.Length;

        /// <summary>VS indices of the parameter slots.</summary>
        public ReadOnlySpan<int> Slots => _slots;

        /// <summary>Overwrite the frame with values from VS indices.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFromVirtualStack(ReadOnlySpan<int> vsIndices)
        {
            if (vsIndices.Length != _slots.Length)
                throw new ArgumentException("Length mismatch.", nameof(vsIndices));

            _acl.ZeroExisting(_slots);
            _acl.IncrementArrayBy(_slots, targetOriginals: false, indicesOfIncrements: vsIndices.ToArray());
        }

        /// <summary>
        /// Overwrite the frame with values from original sources. Uses NextSource
        /// semantics when ordered mode is enabled; otherwise falls back to CopyTo.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetFromOriginalSources(ReadOnlySpan<int> originalIndices)
        {
            if (originalIndices.Length != _slots.Length)
                throw new ArgumentException("Length mismatch.", nameof(originalIndices));

            for (int i = 0; i < _slots.Length; i++)
            {
                int tmp = _r.CopyToNew(originalIndices[i], fromOriginalSources: true);
                _r.CopyToExisting(_slots[i], tmp);
            }
        }
    }
}
