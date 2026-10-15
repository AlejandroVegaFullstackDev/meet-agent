using MeetAgent.Cli;
using MeetAgent.Cli.Commands;

var args0 = new Args(args);
return args0.Command switch
{
    "capture" => await CaptureCommand.RunAsync(args0),
    _ => Help(),
};

static int Help()
{
    Console.WriteLine("""
        MeetAgent — PoC local
          capture --seconds 60 [--min-dbfs -60]     graba data/mic.wav y data/loopback.wav
        """);
    return 1;
}
