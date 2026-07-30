using System;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class SaveService
{
    private readonly string _path;
    private bool _loadWarningLogged;

    public SaveService()
    {
        _path = Path.Combine(Application.persistentDataPath, "save.json");
    }

    public bool HasSave => File.Exists(_path);

    public void Save(SaveData data)
    {
        string tempPath = _path + ".tmp";

        if (File.Exists(tempPath))
            File.Delete(tempPath);

        string json = JsonUtility.ToJson(data, prettyPrint: true);
        byte[] bytes = Protect(json);
        File.WriteAllBytes(tempPath, bytes);

        if (File.Exists(_path))
            File.Replace(tempPath, _path, null);
        else
            File.Move(tempPath, _path);
    }

    public bool TryLoad(out SaveData data)
    {
        data = null;

        if (!HasSave)
            return false;

        try
        {
            byte[] bytes = File.ReadAllBytes(_path);
            string json = Unprotect(bytes);
            data = JsonUtility.FromJson<SaveData>(json);
        }
        catch (Exception exception)
        {
            WarnLoadFailed("Failed to parse save data: " + exception.Message);
            data = null;
            return false;
        }

        if (data == null)
        {
            WarnLoadFailed("Save data was empty or invalid.");
            return false;
        }

        if (data.version != SaveData.CurrentVersion)
        {
            WarnLoadFailed("Save version mismatch. Expected " + SaveData.CurrentVersion + ", got " + data.version + ".");
            data = null;
            return false;
        }

        if (data.items == null)
            data.items = new System.Collections.Generic.List<ItemStackSave>();

        if (data.enhancements == null)
            data.enhancements = new System.Collections.Generic.List<EnhancementSave>();

        if (data.unlockedPassiveIds == null)
            data.unlockedPassiveIds = new System.Collections.Generic.List<string>();

        return true;
    }

    public void Delete()
    {
        if (HasSave)
            File.Delete(_path);
    }

    private void WarnLoadFailed(string message)
    {
        string backupPath = Path.ChangeExtension(_path, ".bak");
        string backupMessage;

        try
        {
            File.Copy(_path, backupPath, overwrite: true);
            backupMessage = " Backup created at: " + backupPath + ".";
        }
        catch (Exception exception)
        {
            backupMessage = " Backup failed: " + exception.Message + ".";
        }

        if (_loadWarningLogged)
            return;

        _loadWarningLogged = true;
        Debug.LogWarning("[SaveService] " + message + " Save ignored." + backupMessage);
    }

    private static byte[] Protect(string json)
    {
        // Later insertion point for AES/HMAC.
        return Encoding.UTF8.GetBytes(json);
    }

    private static string Unprotect(byte[] bytes)
    {
        // Later insertion point for AES/HMAC.
        return Encoding.UTF8.GetString(bytes);
    }
}
