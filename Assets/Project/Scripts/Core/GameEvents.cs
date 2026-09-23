using System;
using UnityEngine;

public static class GameEvents
{
    // Player → Invigilator
    public static event Action OnPhoneShown;
    public static event Action OnPhoneHidden;
    public static event Action OnBuzz;              // phone vibrated (noise)
    public static event Action<int, int> OnAnswerRevealed; // (questionIndex, choice 0-3)

    // Invigilator/GameManager → Player
    public static event Action OnCaught;
    public static event Action OnExamEnded;

    public static void PhoneShown() => OnPhoneShown?.Invoke();
    public static void PhoneHidden() => OnPhoneHidden?.Invoke();
    public static void Buzz() => OnBuzz?.Invoke();
    public static void AnswerRevealed(int q, int c) => OnAnswerRevealed?.Invoke(q, c);
    public static void Caught() => OnCaught?.Invoke();
    public static void ExamEnded() => OnExamEnded?.Invoke();
}