#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace CustomChirps.Utils;

public class RuntimeLocalizationWindow(Dictionary<string, string> runtimeDict)
{
    private const int WindowSize = 144;
    private readonly LinkedList<string> _keyList = [];

    private readonly Dictionary<string, string> _runtimeDict =
        runtimeDict ?? throw new ArgumentNullException(nameof(runtimeDict));

    public void AddWithWindowManagement(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (_keyList.Contains(key))
        {
            RemoveKey(key);
        }

        if (_keyList.Count >= WindowSize)
        {
            RemoveOldestKey();
        }

        _runtimeDict[key] = value;
        _keyList.AddLast(key);
    }

    private void RemoveOldestKey()
    {
        if (_keyList.Count == 0) return;

        var oldestKey = _keyList.First?.Value;
        if (oldestKey != null)
        {
            _keyList.RemoveFirst();
            _runtimeDict.Remove(oldestKey);
        }
    }

    private void RemoveKey(string key)
    {
        if (_runtimeDict.Remove(key))
        {
            _keyList.Remove(key);
        }
    }

    public int Count => _keyList.Count;
    public static int MaxSize => WindowSize;

    public void Clear()
    {
        foreach (var key in _keyList)
        {
            _runtimeDict.Remove(key);
        }

        _keyList.Clear();
    }

    public bool ContainsKey(string key)
    {
        return _keyList.Contains(key);
    }

    public string[] GetAllKeys()
    {
        return _keyList.ToArray();
    }
}