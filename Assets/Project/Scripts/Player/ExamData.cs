using System;
using System.Collections.Generic;
using UnityEngine;

public enum Difficulty { Easy, Medium, Hard, Extreme, Impossible }

[Serializable]
public class Question
{
    [TextArea(2, 4)] public string text;
    public string[] options = new string[4];
    [Range(0, 3)] public int correctIndex;
    public Difficulty difficulty;
}

// The exam: questions, answers, and how long each question's combo is.
// Create via: Project window > right-click > Create > EyesOnPaper > Exam Questions
[CreateAssetMenu(fileName = "ExamQuestions", menuName = "EyesOnPaper/Exam Questions")]
public class ExamData : ScriptableObject
{
    public string examTitle = "APPLIED NONSENSE 101 - FINAL EXAM";
    public List<Question> questions = new List<Question>();

    [Header("Combo length by difficulty")]
    public int easyLength = 4;
    public int mediumLength = 6;
    public int hardLength = 8;
    public int extremeLength = 10;
    public int impossibleLength = 12;

    public int Count => questions.Count;

    public int ComboLength(int questionIndex) => questions[questionIndex].difficulty switch
    {
        Difficulty.Easy   => easyLength,
        Difficulty.Medium => mediumLength,
        Difficulty.Hard   => hardLength,
        Difficulty.Extreme => extremeLength,
        Difficulty.Impossible => impossibleLength,
        _ => mediumLength
    };

    public static string Letter(int choice) => ((char)('A' + choice)).ToString();

    // In the asset's Inspector: click the ⋮ menu (top right) > "Fill Sample Questions"
    [ContextMenu("Fill Sample Questions")]
    void FillSampleQuestions()
    {
        questions = new List<Question>
        {
            Q(Difficulty.Easy, "In the Thermodynamics of Procrastination, a study session reaches heat death:",
              new[] { "After one YouTube video", "At 11:59 PM", "At 3:00 AM", "Never" }, 0),
            Q(Difficulty.Easy, "Which pencil grade is legally required for bubble sheets in the Republic of Exams?",
              new[] { "HB", "2B", "4H", "A pen, out of spite" }, 1),
            Q(Difficulty.Easy, "Solve for x, where x is the number of times you have checked the clock.",
              new[] { "3", "12", "Too many", "x + 1" }, 3),
            Q(Difficulty.Medium, "The Third Law of Group Projects states:",
              new[] { "For every action, one person does it", "Work expands to fill the deadline", "Energy is never conserved", "The quiet one knows everything" }, 0),
            Q(Difficulty.Medium, "What is the primary export of the Faculty of Arts?",
              new[] { "Essays", "Coffee cups", "Opinions", "Tote bags" }, 3),
            Q(Difficulty.Medium, "Which of these is NOT a recognised state of matter?",
              new[] { "Solid", "Plasma", "Soggy", "Bose-Einstein condensate" }, 2),
            Q(Difficulty.Hard, "Convert 450 pounds of jet fuel to kilograms.",
              new[] { "About 204 kg", "About 450 kg", "About 992 kg", "Ask the pilot" }, 0),
            Q(Difficulty.Hard, "In Reason's error taxonomy, forgetting a step in a checklist is a:",
              new[] { "Slip", "Lapse", "Mistake", "Violation" }, 1),
            Q(Difficulty.Hard, "Under exam conditions, the half-life of a memorised answer is:",
              new[] { "Until you look up", "Four seconds", "One question", "Longer than you think" }, 0),
            Q(Difficulty.Extreme, "A bubble sheet shifted down one row after Q3 can cause how many wrong answers out of 10?",
              new[] { "1", "3", "Up to 7", "0" }, 2),
            Q(Difficulty.Extreme, "What is the invigilator thinking about right now?",
              new[] { "Lunch", "You", "Retirement", "The heat death of the universe" }, 1),
            Q(Difficulty.Extreme, "A student checks their sheet twice and changes nothing. They have committed:",
              new[] { "A slip", "A lapse", "No error at all", "The only correct act in this room" }, 2),
            Q(Difficulty.Impossible, "You enter an arrow pattern that is one input wrong. The phone will:",
              new[] { "Refuse it", "Show the right answer anyway", "Vibrate", "Show a wrong answer, confidently" }, 3),
            Q(Difficulty.Impossible, "The invigilator stops walking. This means:",
              new[] { "He is tired", "He is watching someone", "Nothing at all", "You have already lost" }, 1),
            Q(Difficulty.Impossible, "The most dangerous moment in an exam is:",
              new[] { "The first question", "The final minute", "The moment you feel safe", "The moment the room empties" }, 2),
        };
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    static Question Q(Difficulty d, string text, string[] opts, int correct) =>
        new Question { difficulty = d, text = text, options = opts, correctIndex = correct };
}
