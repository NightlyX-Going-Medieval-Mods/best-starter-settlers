// Runs coroutine-style sequences without a MonoBehaviour. Going Medieval destroys BepInEx's plugin
// GameObject after start-up, so Plugin.StartCoroutine throws. This ticks from a static UI event
// instead (the character editor is UI, so it renders every frame).
// Inside a routine: `yield return 0.2f` waits 0.2 s (real time), `yield return null` waits a frame,
// `yield return otherRoutine` runs a nested IEnumerator.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BestStarter.Game
{
    internal static class Scheduler
    {
        sealed class Routine
        {
            public readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
            public float ResumeAt;
            public Action<Exception> OnError;
        }

        static readonly List<Routine> routines = new List<Routine>();
        static bool hooked;
        static int lastFrame = -1;

        internal static void Start(IEnumerator routine, Action<Exception> onError)
        {
            if (!hooked) { Canvas.willRenderCanvases += Tick; hooked = true; }
            var r = new Routine { OnError = onError };
            r.Stack.Push(routine);
            routines.Add(r);
        }

        static void Tick()
        {
            if (Time.frameCount == lastFrame) return;   // willRenderCanvases can fire more than once a frame
            lastFrame = Time.frameCount;
            float now = Time.realtimeSinceStartup;
            foreach (var r in routines.ToArray())
            {
                if (now < r.ResumeAt) continue;
                try { Step(r, now); }
                catch (Exception e)
                {
                    routines.Remove(r);
                    Plugin.Log.LogError(e);
                    try { r.OnError?.Invoke(e); } catch (Exception e2) { Plugin.Log.LogError(e2); }
                }
            }
        }

        static void Step(Routine r, float now)
        {
            while (r.Stack.Count > 0)
            {
                var it = r.Stack.Peek();
                if (!it.MoveNext()) { r.Stack.Pop(); continue; }
                switch (it.Current)
                {
                    case IEnumerator nested: r.Stack.Push(nested); continue;
                    case float seconds: r.ResumeAt = now + seconds; return;
                    default: return;   // null or anything else: next frame
                }
            }
            routines.Remove(r);
        }
    }
}
