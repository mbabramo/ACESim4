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
        public bool ManageDepthScopes { get; init; } = true; // DEBUG
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

        // Per-set context so multiple sets can be open (nested) at once.
        private readonly struct SetCtx
        {
            public readonly string Name;
            public readonly int? FirstStart;    // null until first action begins
            public readonly int  ActionCount;   // number of actions begun so far

            public SetCtx(string name, int? firstStart, int actionCount)
            {
                Name = name;
                FirstStart = firstStart;
                ActionCount = actionCount;
            }

            public SetCtx WithFirstStart(int s) => new SetCtx(Name, s, ActionCount);
            public SetCtx IncrementActions()    => new SetCtx(Name, FirstStart, ActionCount + 1);
        }

        // LIFO stack of open sets
        private System.Collections.Generic.Stack<SetCtx> _stack = new System.Collections.Generic.Stack<SetCtx>();

        public IdenticalRangeTemplate(ArrayCommandList acl, RegionTemplateOptions options = null)
        {
            _acl = acl ?? throw new ArgumentNullException(nameof(acl));
            _opts = options ?? new RegionTemplateOptions();
        }

        /// <summary>Begin an identical set. Dispose to close the set.</summary>
        public IDisposable BeginSet(string setName)
        {
            string name = setName ?? "IdenticalSet";
            _stack.Push(new SetCtx(name, firstStart: null, actionCount: 0));

            if (_opts.IncludeComments)
                _acl.InsertComment($"[IDENTICAL-BEGIN] set={name}");

            return new SetScope(this);
        }

        /// <summary>
        /// Begin an action body within the current set. Dispose to close the action chunk.
        /// The first action is recorded; subsequent actions replay by span.
        /// </summary>
        public IDisposable BeginAction(string actionLabel)
        {
            if (_stack.Count == 0)
                throw new InvalidOperationException("BeginSet must be called before BeginAction.");

            // Work with the current set context (top of stack)
            var ctx = _stack.Pop();
            bool isFirst = ctx.ActionCount == 0;

            string chunkName = string.IsNullOrEmpty(_opts.ChunkNamePrefix)
                ? $"Identical:{ctx.Name}"
                : $"{_opts.ChunkNamePrefix}:Identical:{ctx.Name}";

            int? identicalStart = isFirst ? (int?)null : ctx.FirstStart;
            _acl.StartCommandChunk(runChildrenInParallel: false,
                                   identicalStartCommandRange: identicalStart,
                                   name: chunkName);

            if (isFirst && ctx.FirstStart == null)
            {
                // Record the command-index where this body's recording begins;
                // replays will jump back to this start.
                ctx = ctx.WithFirstStart(_acl.NextCommandIndex);
            }

            if (_opts.IncludeComments)
                _acl.InsertComment($"[IDENTICAL-ACTION] set={ctx.Name} action={actionLabel}");

            if (_opts.ManageDepthScopes)
                _acl.Recorder.IncrementDepth();

            // Push updated context back with incremented action count
            _stack.Push(ctx.IncrementActions());

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

                if (_t._stack.Count > 0)
                {
                    var ended = _t._stack.Pop();
                    if (_t._opts.IncludeComments)
                        _t._acl.InsertComment($"[IDENTICAL-END] set={ended.Name}");
                }
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
        public bool IsOpen => _inWindow;

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

            // Copy values directly; CopyToExisting is replay-aware.
            for (int i = 0; i < _slots.Length; i++)
                _acl.CopyToExisting(_slots[i], vsIndices[i]);
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
