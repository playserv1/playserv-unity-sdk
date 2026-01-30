using System;
using UnityEngine;

namespace Playserv.Spawn
{
    public sealed class Interpolator<T>
    {
        private readonly Func<T, T, float, T> _lerpFunc;

        public T Current { get; set; }
        public T Target { get; private set; }
        public bool HasTarget { get; private set; }

        public Interpolator(T initial, Func<T, T, float, T> lerpFunc)
        {
            _lerpFunc = lerpFunc;
            Current = initial;
            Target = initial;
        }

        public void SetTarget(T target)
        {
            Target = target;
            HasTarget = true;
        }

        public void Update(float speed)
        {
            if (!HasTarget)
                return;

            Current = _lerpFunc(Current, Target, speed);
        }

        public void Reset(T value)
        {
            Current = value;
            Target = value;
            HasTarget = false;
        }

        public static Interpolator<Vector3> CreateVector3(Vector3 initial) =>
            new(initial, Vector3.Lerp);

        public static Interpolator<Quaternion> CreateQuaternion(Quaternion initial) =>
            new(initial, Quaternion.Slerp);

        public static Interpolator<float> CreateFloat(float initial) =>
            new(initial, Mathf.Lerp);

        public static Interpolator<Color> CreateColor(Color initial) =>
            new(initial, Color.Lerp);
    }
}
