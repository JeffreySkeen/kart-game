using System;
using UnityEngine;

/// <summary>
/// Trigger on the start/finish line. The first forward crossing starts the race;
/// every later forward crossing completes a lap. Driving backward through it is ignored.
/// The object's forward (+Z) points in the direction of travel.
/// </summary>
public class FinishLine : MonoBehaviour
{
    // Static so listeners don't need a reference to this object, which TrackGenerator recreates on rebuild.
    public static event Action RaceStarted;
    public static event Action<int> LapCompleted;

    public int LapsCompleted { get; private set; }

    bool raceStarted;

    void OnTriggerEnter(Collider other)
    {
        var player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        var body = player.GetComponent<Rigidbody>();
        if (body == null || Vector3.Dot(body.linearVelocity, transform.forward) <= 0f) return;

        if (!raceStarted)
        {
            raceStarted = true;
            Debug.Log("Race started");
            RaceStarted?.Invoke();
            return;
        }

        LapsCompleted++;
        Debug.Log($"Lap {LapsCompleted} complete");
        LapCompleted?.Invoke(LapsCompleted);
    }
}
