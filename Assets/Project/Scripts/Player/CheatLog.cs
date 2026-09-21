using System;
using System.Collections.Generic;
using UnityEngine;

public enum CheatSource { Self, Jack }

// Shared record of every answer the player has seen, and every Jack message they missed.
// The results screen compares this against what was written on the answer sheet.
public static class CheatLog
{
    public class Entry
    {
        public int question;        // 0-based
        public int answer;          // 0-3 (A-D), always the correct one
        public CheatSource source;
        public float time;
        public int batch;           // Jack messages: answers from the same text share a batch id
    }

    public class Missed
    {
        public List<int> questions;
        public float time;
    }

    static readonly List<Entry> entries = new List<Entry>();
    static readonly List<Missed> missed = new List<Missed>();

    public static IReadOnlyList<Entry> Entries => entries;
    public static IReadOnlyList<Missed> MissedMessages => missed;

    public static event Action<Entry> OnRevealed;

    // "Is this question done?" Defaults to "has its answer been revealed".
    // AnswerSheet will replace this with "has something been written on the sheet",
    // so a forgotten Jack answer sends your own cheat back to that question.
    public static Func<int, bool> IsAnswered = IsRevealed;

    public static bool IsRevealed(int q) => entries.Exists(e => e.question == q);

    public static int NextUnanswered(int questionCount)
    {
        for (int q = 0; q < questionCount; q++)
            if (!IsAnswered(q)) return q;
        return -1;
    }

    public static void Reveal(int question, int answer, CheatSource source, int batch = 0)
    {
        var e = new Entry { question = question, answer = answer, source = source, time = Time.time, batch = batch };
        entries.Add(e);
        OnRevealed?.Invoke(e);
        GameEvents.AnswerRevealed(question, answer);
    }

    public static void MissJack(List<int> questions)
    {
        missed.Add(new Missed { questions = new List<int>(questions), time = Time.time });
    }

    // Called automatically when Play starts. Call again when restarting the exam.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset()
    {
        entries.Clear();
        missed.Clear();
        OnRevealed = null;
        IsAnswered = IsRevealed;
    }
}
