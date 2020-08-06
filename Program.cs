// #define ALL_PROFILES
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aurio.Matching.HaitsmaKalker2002;

namespace SyncVideoWithAudio
{
    static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 3 || args.Contains("--help"))
            {
                Console.WriteLine("use as: SyncVideoWithAudio.exe <offset|align> <videofile> <audiofile> [duration] [profile]");
                return 1;
            }
            else if (args[0].ToLower() != "offset" && args[0].ToLower() != "align")
            {
                Console.WriteLine("argument 1 must be offset or align");
                return 2;
            }
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            List<string> descriptors = new List<string>();
            List<Profile> profiles = new List<Profile>(FingerprintGenerator.GetProfiles());
            Profile humanProfile = profiles[4];
            profiles.RemoveAt(4);
            profiles.Insert(0, humanProfile);
            if (args.Length >= 5)
            {
                try
                {
                    var swapPos = int.Parse(args[4]);
                    if (swapPos < 5 && swapPos >= 0)
                    {
                        Profile swapProfile = profiles[swapPos];
                        profiles.RemoveAt(swapPos);
                        profiles.Insert(0, humanProfile);
                    }
                }
                catch
                {
                }
            }

            foreach (Profile profile in profiles)
            {
                Console.WriteLine(profile.Name);
                try
                {
                    OffsetAnalysis offsetAnalysis = new OffsetAnalysis(args[1], args[2], profile);
                    TimeSpan offset;
                    long tickCount;
                    try
                    {
                        var timeSplit = args[3].Split(':').ToList().Select(double.Parse).Reverse();
                        IEnumerable<double> times = timeSplit as double[] ?? timeSplit.ToArray();
                        tickCount = (long) (times.ElementAt(0) * TimeSpan.TicksPerSecond);
                        if (times.Count() > 1)
                        {
                            tickCount += (long) times.ElementAtOrDefault(1) * TimeSpan.TicksPerMinute;
                            if (times.Count() > 2)
                                tickCount += (long) times.ElementAtOrDefault(2) * TimeSpan.TicksPerHour;
                        }
                    }
                    catch
                    {
                        tickCount = TimeSpan.TicksPerHour*5;
                    }

                    TimeSpan songLength = new TimeSpan(tickCount);
                    if (args[0] == "offset")
                    {
                        offset = offsetAnalysis.GenerateSections(songLength, true);
                        Console.WriteLine($"Results: {offset.TotalMilliseconds} {offsetAnalysis.NeedsCut} {offsetAnalysis.CommandAsStrings}");
#if !ALL_PROFILES
                        return 0;
#else
                        continue;
#endif
                    }

                    offset = offsetAnalysis.GenerateSections(songLength);
                    descriptors.Add($"{offsetAnalysis.descriptor} {offsetAnalysis.PowerShellDemuxCommand} {profile.Name}");
                    Console.WriteLine($"Results: {offset.TotalMilliseconds} {offsetAnalysis.NeedsCut} {offsetAnalysis.CommandAsStrings}");
#if !ALL_PROFILES
                    break;
#endif
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(profile.Name);
                    Console.Error.WriteLine(e);
                    descriptors.Add($"{profile.Name} Failed");
                    continue;
                }
            }
            // descriptors.ForEach(Console.WriteLine);
            // Console.WriteLine(new FileInfo(args[1]).FullName);
            // Console.ReadLine();
            return 0;
#if false
            //Tests: Signal, DTNA, Roly-Poly, Fake Love, Gogobebe
            // Use PFFFT as FFT implementation
            FFTFactory.Factory = new Aurio.PFFFT.FFTFactory();
            // Use Soxr as resampler implementation
            ResamplerFactory.Factory = new Aurio.Soxr.ResamplerFactory();
            // Use FFmpeg for file reading/decoding
            AudioStreamFactory.AddFactory(new Aurio.FFmpeg.FFmpegAudioStreamFactory());
            // Setup the sources

            // var audioTrack1 = new AudioTrack(new FileInfo(@"D:\SteamLibrary\steamapps\common\Beat Saber\Beat Saber_Data\CustomLevels\30aa (Red Velvet  'Rookie' - Red Velvet)\Red Velvet 레드벨벳 'Rookie' MV.mp4"));
            // var audioTrack2 = new AudioTrack(new FileInfo(@"D:\SteamLibrary\steamapps\common\Beat Saber\Beat Saber_Data\CustomLevels\30aa (Red Velvet  'Rookie' - Red Velvet)\song.egg"));
            //AudioTrack audioTrack1 = new AudioTrack(new FileInfo("mv.wav"));
            //AudioTrack audioTrack2 = new AudioTrack(new FileInfo("song.wav"));
            if(args.Length < 2)
                throw new Exception("Not enough input files");
            // string[] audioFiles = { "crush.mp4", "crush.m4a"};
            string[] audioFiles = { args[0], args[1] };
            // Setup the fingerprint profile
            Profile profile =
 FingerprintGenerator.GetProfiles()[2]; // DefaultProfile(), BugProfile(), VoiceProfile(), BassProfile(), HumanProfile()

            // Create a fingerprint store
            FingerprintStore store = new FingerprintStore(profile) {Threshold = .95f};

            foreach (string audioFile in audioFiles)
            {
                Console.WriteLine($"Generating fp for {audioFile}");
                AudioTrack audioTrack = new AudioTrack(new FileInfo(audioFile));
                FingerprintGenerator fpg = new FingerprintGenerator(profile, audioTrack);
                int subFingerprintsCalculated = 0;
                fpg.SubFingerprintsGenerated += delegate(object s2, SubFingerprintsGeneratedEventArgs e2)
                {
                    subFingerprintsCalculated++;
                    store.Add(e2);
                };
                fpg.Generate();
                //store.Analyze();
            }
            Console.WriteLine("Matching");
            List<Match> matches = store.FindAllMatches();
            // Check if tracks match
            if (matches.Count > 0)
            {
                Console.WriteLine("overlap detected!");
            }
            List<OffsetData> offsetDatas = new List<OffsetData>();
            foreach (Match match in matches)
            {
                if (match.Similarity < .3f) continue;
                TimeSpan matchOffset = match.Offset;
                TimeSpan videoTime = match.Track1Time;
                Console.WriteLine(
                    $"{match.Track1.FileInfo.Name}-{match.Track2.FileInfo.Name}\t: {matchOffset:g} @ {videoTime:g} {match.Similarity * 100:F1}%");
                foreach (OffsetData offsetData in offsetDatas)
                {
                    TimeSpan oneSecond = new TimeSpan(0, 0, 1);
                    if (offsetData.IsWithin(matchOffset, oneSecond))
                    {
                        offsetData.Add(matchOffset, videoTime, match.Similarity);
                        goto Matched;
                    }
                }

                //Run Only if No Matches
                offsetDatas.Add(new OffsetData(matchOffset, videoTime, match.Similarity));
                Matched: ;
            }

            offsetDatas.Sort((a, b) => a.FirstOccurrence().CompareTo(b.FirstOccurrence()));
            ushort section = 0;
            List<OffsetData> useableOffsets = new List<OffsetData>();
            var maxOccurences = offsetDatas.Max(a => a.Occurrences) / 4;
            foreach (OffsetData offsetData in offsetDatas)
            {
                if (offsetData.Confidence > .45f && offsetData.Occurrences > maxOccurences)
                {
                    useableOffsets.Add(offsetData);
                    Console.WriteLine(offsetData);
                }
            }

            // List<string> outputFiles = new List<string>();
            
            //First One
            OffsetData thisOffsetData;
            OffsetData nextOffsetData;
            TimeSpan start;
            TimeSpan length;
            string ffmpegInputs = "";
            List<string> demuxFile = new List<string>();
            thisOffsetData = useableOffsets[0];
            nextOffsetData = useableOffsets[1];
            start = thisOffsetData.Offset.Ticks > 0 ? thisOffsetData.Offset : new TimeSpan(0);
            TimeSpan timeToAddBetweenNextClips = nextOffsetData.FirstOccurrence() //"Supposed" start of next clip
                .Subtract(thisOffsetData.LastOccurrence()) //Minus end of this clip is how much time is now between
                .Subtract( //minus
                    nextOffsetData.Offset.Subtract(thisOffsetData.Offset) //How much time should be between
                ); //is how much needs to be added
            TimeSpan end = thisOffsetData.LastOccurrence().Add(new TimeSpan(timeToAddBetweenNextClips.Ticks / 2));
            length = end.Subtract(start);
            Console.WriteLine($"{thisOffsetData.Offset:g} Starting at {start} ending at {end} occuring {thisOffsetData.Occurrences} times with confidence {thisOffsetData.Confidence * 100:F1}%");
            ffmpegInputs += MakeFfmpegInput(audioFiles, start, length, ++section);
            demuxFile.AddRange(MakeFfmpegDemuxInput(audioFiles, start, end, length));
            TimeSpan timeToAddBetweenLastClip = timeToAddBetweenNextClips;
            for (int i = 1; i <= useableOffsets.Count - 2; i++)
            {
                thisOffsetData = useableOffsets[i];
                nextOffsetData = useableOffsets[i + 1];
                OffsetData prevOffsetData = useableOffsets[i - 1];
                timeToAddBetweenNextClips = nextOffsetData.FirstOccurrence() //"Supposed" start of next clip
                    .Subtract(thisOffsetData.LastOccurrence()) //Minus end of this clip is how much time is now between
                    .Subtract( //minus
                        nextOffsetData.Offset.Subtract(thisOffsetData.Offset) //How much time should be between
                    ); //is how much needs to be added
                start =
 thisOffsetData.FirstOccurrence().Subtract(new TimeSpan(timeToAddBetweenLastClip.Ticks / 2)); // move start back by half of what needs to be added back
                end =
 thisOffsetData.LastOccurrence().Add(timeToAddBetweenNextClips); // move end forward by half of what needs to be added back
                length = end.Subtract(start);
                Console.WriteLine($"{thisOffsetData.Offset:g} Starting at {start} ending at {end} occuring {thisOffsetData.Occurrences} times with confidence {thisOffsetData.Confidence * 100:F1}%");
                ffmpegInputs += MakeFfmpegInput(audioFiles, start, length, ++section);
                demuxFile.AddRange(MakeFfmpegDemuxInput(audioFiles, start, end, length));
                timeToAddBetweenLastClip = timeToAddBetweenNextClips;
            }
            //last clip
            OffsetData lastOffsetData = useableOffsets[useableOffsets.Count - 1];
            start = lastOffsetData.FirstOccurrence().Subtract(new TimeSpan(timeToAddBetweenLastClip.Ticks / 2));
            end = lastOffsetData.LastOccurrence().Add(new TimeSpan(0, 0, 30));
            length = end.Subtract(start);
            Console.WriteLine($"{lastOffsetData.Offset:g} Starting at {start} ending at {lastOffsetData.LastOccurrence()} occuring {lastOffsetData.Occurrences} times with confidence {lastOffsetData.Confidence * 100:F1}%");
            ffmpegInputs += MakeFfmpegInput(audioFiles, start, length, ++section);
            demuxFile.AddRange(MakeFfmpegDemuxInput(audioFiles, start, end, length));
            string inputs = "";
            string filter_complex = " -filter_complex '";
            string test =
                "ffmpeg -i part1.mp4 -i part2.mp4 -filter_complex '[0:0][0:1][1:0][1:1]concat=n=2:v=1:a=1 [v] [a1]' -map '[v]' -map '[a1]' output.mp4";
            int sectionCount = Regex.Matches(ffmpegInputs, "-i").Count;
            for (int i = 0; i < sectionCount; i++)
            {
                // inputs += $" -i {outputFiles[i]}";
                filter_complex += $"[{i}:0][{i}:1]";
            }
            string ffmpegCommand =
 $"ffmpeg{inputs}{filter_complex}concat=n={sectionCount}:v=1:a=1 [v] [a1]' -map '[v]' -map '[a1]' {audioFiles[0].Split('.')[0]}-cut.mp4";
            Console.WriteLine(ffmpegCommand);
            File.WriteAllLines("demuxConfig.txt", demuxFile);
            // Console.WriteLine("Press Enter To Quit");
            // Console.ReadLine();
            return 0;
#endif
        }

#if false
        private static string MakeFfmpegInput(string[] audioFiles, TimeSpan start, TimeSpan length, ushort section)
        {
            return $"-ss {start} -t {length} -i \"{audioFiles[0]}\"";
        }
        private static string[] MakeFfmpegDemuxInput(string[] audioFiles, TimeSpan start, TimeSpan end, TimeSpan length)
        {
            return new[] {$"file {audioFiles[0]}", $"inpoint {start}", $"duration {length}", $"outpoint {end}", ""};
        }
#endif
    }
}