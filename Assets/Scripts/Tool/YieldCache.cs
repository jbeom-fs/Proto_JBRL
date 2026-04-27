using System.Collections;
using System.Collections.Generic;
using UnityEngine;

static class YieldCache
{
    class FloatComparer : IEqualityComparer<float>
    {
        bool IEqualityComparer<float>.Equals(float x, float y)
        {
            return x == y;
        }
        int IEqualityComparer<float>.GetHashCode(float obj)
        {
            return obj.GetHashCode();
        }
    }

    public static readonly WaitForEndOfFrame WaitForEndOfFrame = new WaitForEndOfFrame();
    public static readonly WaitForFixedUpdate WaitForFixedUpdate = new WaitForFixedUpdate();

    private static readonly Dictionary<float, WaitForSeconds> _timeInterval = new Dictionary<float, WaitForSeconds>(new FloatComparer());
    private static readonly Dictionary<float, WaitForSecondsRealtime> _timeIntervalReal = new Dictionary<float, WaitForSecondsRealtime>(new FloatComparer());

    public static WaitForSeconds WaitForSeconds(float seconds)
    {
        WaitForSeconds wfs;
        if (!_timeInterval.TryGetValue(seconds, out wfs))
            _timeInterval.Add(seconds, wfs = new WaitForSeconds(seconds));
        return wfs;
    }

    public static WaitForSecondsRealtime WaitForSecondsRealTime(float seconds)
    {
        WaitForSecondsRealtime wfsReal;
        if (!_timeIntervalReal.TryGetValue(seconds, out wfsReal))
            _timeIntervalReal.Add(seconds, wfsReal = new WaitForSecondsRealtime(seconds));
        return wfsReal;
    }
}