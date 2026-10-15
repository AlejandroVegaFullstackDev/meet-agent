using MeetAgent.Cli;
using MeetAgent.Cli.Commands;

var args0 = new Args(args);
return args0.Command switch
{
    "capture" => await CaptureCommand.RunAsync(args0),
    "live" => await LiveCommand.RunAsync(args0),
    "bench" => await BenchCommand.RunAsync(args0),
    "bench-one" => await BenchCommand.RunOneAsync(args0),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine("""
        MeetAgent — PoC local
          capture --seconds 60 [--min-dbfs -60]     graba data/mic.wav y data/loopback.wav
          live [--model small-q5_1|medium-q5_0] [--seconds N] [--threads N] [--record]
               [--mic-wav a.wav] [--loop-wav b.wav] [--fast] [--out transcript.txt]
          bench --audio a.wav --ref ref.txt [--models small-q5_1,medium-q5_0] [--modes chunked,whole]
        """);
    return 1;
}
