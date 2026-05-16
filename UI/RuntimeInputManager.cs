using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class RuntimeInputManager : MonoBehaviour
{
    public static RuntimeInputManager Instance { get; private set; }

    private Dictionary<string, KeyCode> keys = new Dictionary<string, KeyCode>();

    private static readonly Dictionary<string, KeyCode> defaultKeys = new Dictionary<string, KeyCode>
    {
        { "Brake", KeyCode.Space },
        { "ToggleAuto", KeyCode.T },
        { "SwitchCam", KeyCode.C },
        { "ToggleUI", KeyCode.Escape }
    };

    public delegate void KeyReboundHandler(string actionName, KeyCode newKey);
    public event KeyReboundHandler OnKeyRebound;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadKeys();
    }

    void LoadKeys()
    {
        keys.Clear();
        foreach (var kvp in defaultKeys)
        {
            string saved = PlayerPrefs.GetString("Keybind_" + kvp.Key, "");
            if (!string.IsNullOrEmpty(saved) && System.Enum.TryParse(saved, out KeyCode parsed))
            {
                keys[kvp.Key] = parsed;
            }
            else
            {
                keys[kvp.Key] = kvp.Value;
            }
        }
    }

    public bool GetKeyDown(string actionName)
    {
        if (keys.TryGetValue(actionName, out KeyCode key))
        {
            return Input.GetKeyDown(key);
        }
        return false;
    }

    public bool GetKey(string actionName)
    {
        if (keys.TryGetValue(actionName, out KeyCode key))
        {
            return Input.GetKey(key);
        }
        return false;
    }

    public KeyCode GetKeyCode(string actionName)
    {
        if (keys.TryGetValue(actionName, out KeyCode key))
        {
            return key;
        }
        return KeyCode.None;
    }

    public IEnumerator RebindKey(string actionName, UnityEngine.UI.Text uiText)
    {
        if (uiText != null)
        {
            uiText.text = "...";
        }

        yield return null;

        bool bound = false;
        float timeout = Time.realtimeSinceStartup + 5f;

        while (!bound && Time.realtimeSinceStartup < timeout)
        {
            if (Input.anyKeyDown)
            {
                foreach (KeyCode kc in System.Enum.GetValues(typeof(KeyCode)))
                {
                    if (Input.GetKeyDown(kc))
                    {
                        if (kc == KeyCode.Escape)
                        {
                            bound = true;
                            break;
                        }

                        if (kc == KeyCode.None || kc == KeyCode.Mouse0 || kc == KeyCode.Mouse1 || kc == KeyCode.Mouse2)
                        {
                            continue;
                        }

                        keys[actionName] = kc;
                        PlayerPrefs.SetString("Keybind_" + actionName, kc.ToString());
                        PlayerPrefs.Save();

                        if (uiText != null)
                        {
                            uiText.text = kc.ToString();
                        }

                        OnKeyRebound?.Invoke(actionName, kc);
                        bound = true;
                        break;
                    }
                }
            }
            yield return null;
        }

        if (!bound && uiText != null)
        {
            uiText.text = GetKeyCode(actionName).ToString();
        }
    }

    public void ResetAllToDefault()
    {
        foreach (var kvp in defaultKeys)
        {
            keys[kvp.Key] = kvp.Value;
            PlayerPrefs.SetString("Keybind_" + kvp.Key, kvp.Value.ToString());
        }
        PlayerPrefs.Save();
    }

    public string[] GetAllActionNames()
    {
        return new List<string>(keys.Keys).ToArray();
    }
}