namespace MazeSolver
{
    // One composer per MusicStyle. Constructors do all allocation; every member here is
    // called from the audio thread and must not allocate or touch Unity APIs.
    public interface IStyleComposer
    {
        void Reset(int seed, MusicSettings settings);
        void Observe(MazeMusicSnapshot snapshot);
        void Complete(SessionOutcome outcome);
        int ComposeBeat(MusicSettings settings, ScoreNote[] buffer);
        int Tempo { get; }
        int Beat { get; }
        bool Finished { get; }
        string Section { get; }
    }
}
