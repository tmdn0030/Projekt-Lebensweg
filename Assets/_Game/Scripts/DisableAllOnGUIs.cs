using UnityEngine;
using System.Reflection;

public class DisableAllOnGUIs : MonoBehaviour
{
    void Awake()
    {
        // Findet alle aktiven MonoBehaviours in der Szene
        MonoBehaviour[] all = FindObjectsOfType<MonoBehaviour>(true);

        foreach (MonoBehaviour mb in all)
        {
            if (mb == null) continue;

            // Prüft, ob die Komponente eine OnGUI-Methode hat
            MethodInfo method = mb.GetType().GetMethod("OnGUI",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (method != null)
            {
                mb.enabled = false; // Deaktiviert das Script komplett
                Debug.Log("OnGUI deaktiviert: " + mb.GetType().Name);
            }
        }
    }
}
