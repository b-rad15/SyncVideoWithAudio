// #define PRINT_OFFSET_INFO
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Aurio;
using Aurio.FFT;
using Aurio.Matching;
using Aurio.Matching.HaitsmaKalker2002;
using Aurio.Project;
using Aurio.Resampler;
using Match = Aurio.Matching.Match;


namespace SyncVideoWithAudio
{
    class OffsetData : IComparable<OffsetData>
    {
        private readonly List<TimeSpan> _offsets;
        private readonly List<TimeSpan> _videoTimes;
        private readonly List<float> _confidences;
        public float Confidence => _confidences.Average();

        public int Occurrences { get; private set; }
        public TimeSpan Offset => new TimeSpan((long) _offsets.Average(timeSpan => timeSpan.Ticks));

        static readonly TimeSpan ThreeSecond = new TimeSpan(0, 0, 3);

        public bool IsWithin(TimeSpan check, TimeSpan range)
        {
            var nowOffset = Offset;
            return nowOffset.Subtract(range) < check && check < nowOffset.Add(range);
        }

        public bool IsWithin(TimeSpan check, TimeSpan range, TimeSpan videoTime)
        {
            var nowOffset = Offset;
            return nowOffset.Subtract(range) < check && check < nowOffset.Add(range) &&
                   LastOccurrence().Add(ThreeSecond) >= videoTime;
        }

        public OffsetData(TimeSpan offset, TimeSpan videoTime, float confidence)
        {
            _offsets = new List<TimeSpan> {offset};
            _videoTimes = new List<TimeSpan> {videoTime};
            _confidences = new List<float> {confidence};
            Occurrences = 1;
        }

        public int Add(TimeSpan offset, TimeSpan videoTime, float confidence)
        {
            _offsets.Add(offset);
            _videoTimes.Add(videoTime);
            _confidences.Add(confidence);
            return ++Occurrences;
        }

        public TimeSpan FirstOccurrence()
        {
            _videoTimes.Sort();
            return _videoTimes[0];
        }

        public TimeSpan LastOccurrence()
        {
            _videoTimes.Sort();
            return _videoTimes[_videoTimes.Count - 1];
        }

        public override string ToString()
        {
            return $"{FirstOccurrence():hh\\:mm\\:ss\\.fff} -> {LastOccurrence():hh\\:mm\\:ss\\.fff} {Offset:hh\\:mm\\:ss\\.fff} {Confidence * 100:F1}% {Occurrences:D4}";
        }

        public int CompareTo(OffsetData obj)
        {
            return this.Occurrences.CompareTo(obj.Occurrences);
        }

        public bool IsWithin(OffsetData obj)
        {
            return (this.FirstOccurrence() > obj.FirstOccurrence() && obj.FirstOccurrence() > this.LastOccurrence()) ||
                   (this.FirstOccurrence() > obj.LastOccurrence() && obj.LastOccurrence() > this.LastOccurrence());
        }

        public uint FilterMatches()
        {
            uint numRemoved = 0;
            var confComparison = Confidence-.1;
            for (int i = _confidences.Count-1; i >= 0; --i)
            {
                var thisConf = _confidences[i];
                if (thisConf < confComparison)
                {
                    _confidences.RemoveAt(i);
                    _offsets.RemoveAt(i);
                    _videoTimes.RemoveAt(i);
                    ++numRemoved;
                }
            }
            return numRemoved;
        }
    }

    [Serializable]
    class OffsetException : Exception
    {
        public OffsetException()
        {

        }

        public OffsetException(OffsetData offset)
            : base($"Error with offset: {offset}")
        {

        }
        public OffsetException(OffsetData offset1, OffsetData offset2)
            : base($"Error with offsets: {offset1} and {offset2}")
        {

        }

    }
    class OffsetAnalysis
    {
        private List<Match> _matches;
        private List<OffsetData> offsetDatas;
        public List<OffsetData> useableOffsets;
        public TimeSpan firstOffset;
        public int Sections => useableOffsets.Count;
        private readonly string videoFile;
        private readonly string songFile;
        // private List<string> ffmpegInputs = new List<string>();
        private string FfmpegInputs
        {
            get
            {
                string result = "";
                StartsAndLengths.ForEach(sAndL => result += $"-ss {sAndL.start} -t {sAndL.length} -i \"{videoFile}\"");
                return result;
            }
        }

        // private List<string> filterComplex = new List<string>();
        private string filterComplex
        {
            get
            {
                string result = "";
                for(byte i = 0; i < (byte)StartsAndLengths.Count; ++i)
                    result += $"[{i}:v:0][{i}:a:0]";
                return result;
            }
        }
        private FingerprintStore store;
        private readonly List<Task> fpTasks = new List<Task>();
        private FileInfo videoFileInfo;
        private FileInfo songFileInfo;
        public bool NeedsCut = false;

        public string descriptor
        {
            get
            {
                var result = "";
                StartsAndLengths.ForEach(sAndL=>result += $"{sAndL.start}->{sAndL.start.Add(sAndL.length)} |");
                return result.TrimEnd(' ', '|');
            }
        }

        public string outputFilename;

        public string FfmpegCommand => $"ffmpeg {FfmpegInputs} -filter_complex '{filterComplex}concat=n={StartsAndLengths.Count}:v=1:a=1 [v] [a1]' -map '[v]' -map '[a1]' \"{outputFilename}\"";

        // public string FfmpegFilterCommand;
        // public string FfmpegDemuxCommand;

        public string PowerShellDemuxCommand
        {
            get
            {
                var command = $"\"{videoFile}\" @(";
                StartsAndLengths.ForEach(array => command += $"@(\"{array.start:g}\", \"{array.length:g}\"),");
                command = command.TrimEnd(',');
                command += $") \"{outputFilename}\"";
                return command;
            }
        }
        public string CommandAsStrings
        {
            get
            {
                var command = $"\"{videoFile}\" \"{outputFilename}\" \"";
                StartsAndLengths.ForEach(array => command += $"{array.start.TotalSeconds},{array.length.TotalSeconds}|");
                command = command.TrimEnd('|');
                command += '\"';
                return command;
            }
        }

        public struct StartAndLength
        {
            public TimeSpan start;
            public TimeSpan length;
        }
        public List<StartAndLength> StartsAndLengths = new List<StartAndLength>();
        public OffsetAnalysis(string videoFile, string songFile, Profile profile = null) // DefaultProfile(), BugProfile(), VoiceProfile(), BassProfile(), HumanProfile()
        {
            if (profile == null) profile = FingerprintGenerator.GetProfiles()[4];
            this.videoFile = videoFile;
            this.songFile = songFile;
            videoFileInfo = new FileInfo(videoFile);
            songFileInfo = new FileInfo(songFile);
            outputFilename = $"{videoFileInfo.Name.Remove(videoFileInfo.Name.Length-videoFileInfo.Extension.Length)}-cut{videoFileInfo.Extension}";
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // Use PFFFT as FFT implementation
            FFTFactory.Factory = new Aurio.PFFFT.FFTFactory();
            // Use Soxr as resampler implementation
            ResamplerFactory.Factory = new Aurio.Soxr.ResamplerFactory();
            // Use FFmpeg for file reading/decoding
            AudioStreamFactory.AddFactory(new Aurio.FFmpeg.FFmpegAudioStreamFactory());
            // Setup the fingerprint profile
            
            // Create a fingerprint store
            store = new FingerprintStore(profile)
            {
                Threshold = .7f
            };
            void GenerateFingerprint(FileInfo fileInfo)
            {
#if DEBUG
                Console.WriteLine($"Generating fp for {fileInfo.Name}");
#endif
                AudioTrack audioTrack = new AudioTrack(fileInfo);
                FingerprintGenerator fpg = new FingerprintGenerator(profile, audioTrack);
                fpg.SubFingerprintsGenerated += delegate(object s2, SubFingerprintsGeneratedEventArgs e2) { store.Add(e2); };
                fpg.Generate();
            }

            foreach (FileInfo audioFile in new []{videoFileInfo, songFileInfo})
            {
                fpTasks.Add(Task.Run(() =>
                {
                    GenerateFingerprint(audioFile);
#if DEBUG
                    Console.WriteLine($"Finished with: {audioFile.Name}");
#endif
                }));
                // Task.Run(() => GenerateFingerprint(audioFile)).Wait();
            }

        }

        public TimeSpan GenerateSections(TimeSpan songLength = default, bool justOffset = false)
        {
            Task.WaitAll(fpTasks.ToArray());
#if DEBUG
            Console.WriteLine("Matching");
#endif
            _matches = store.FindAllMatches();
            // Check if tracks match
            if (_matches.Count > 0)
            {
#if DEBUG
                Console.WriteLine("overlap detected!");
#endif
            }
            else
            {
                throw new Exception("No Overlap Detected");
            }
            offsetDatas = new List<OffsetData>();
            foreach (Match match in _matches)
            {
                if (match.Similarity < .1f) continue;
                if (match.Track1.FileInfo != videoFileInfo) match.SwapTracks(); 
                TimeSpan matchOffset = match.Offset;
                TimeSpan videoTime = match.Track1Time;
#if DEBUG
                // Console.WriteLine(match.ToString());
#endif
                foreach (OffsetData offsetData in offsetDatas)
                {
                    TimeSpan offsetComparison = new TimeSpan(0, 0, 0, 0, 200);
                    if (offsetData.IsWithin(matchOffset, offsetComparison))
                    {
                        offsetData.Add(matchOffset, videoTime, match.Similarity);
                        goto Matched;
                    }
                }
                //Run Only if No Matches
                offsetDatas.Add(new OffsetData(matchOffset, videoTime, match.Similarity));
                Matched:;
            }
            offsetDatas.Sort((a, b) => a.FirstOccurrence().CompareTo(b.FirstOccurrence()));
            useableOffsets = new List<OffsetData>();
            var maxOccurences = offsetDatas.Max(a => a.Occurrences) / 6;
            foreach (var offsetData in offsetDatas.Where(offsetData => offsetData.Confidence > .45f && offsetData.Occurrences > maxOccurences))
            {
                offsetData.FilterMatches();
                offsetData.FilterMatches();
                useableOffsets.Add(offsetData);
#if DEBUG || PRINT_OFFSET_INFO
                Console.WriteLine(offsetData);
#endif
            }
            this.NeedsCut = useableOffsets.Count > 1;
            if (useableOffsets.Count == 0)
            {
                throw new Exception("No Overlap Detected");
            }
            if (useableOffsets.Count == 1 || justOffset)
            {
                firstOffset = useableOffsets[0].Offset;
                if (songLength != default)
                {
                    StartsAndLengths.Add(new StartAndLength{start = new TimeSpan(0), length = songLength.Add(new TimeSpan(0,0,10))});
                    // FfmpegCommand = $"ffmpeg -i {videoFile} -ss {firstOffset} -t {songLength.Add(new TimeSpan(0, 0, 10))} \"{videoFile.Split('.')[0]}-cut.mp4\"";
                }

                return firstOffset;
            }
            //First One
            OffsetData thisOffsetData;
            OffsetData nextOffsetData;
            TimeSpan start;
            TimeSpan length;
            List<string> demuxFile = new List<string>();
            thisOffsetData = useableOffsets[0];
            nextOffsetData = useableOffsets[1];
            firstOffset = thisOffsetData.Offset.Ticks > 0 ? thisOffsetData.Offset : new TimeSpan(0);
            // start = thisOffsetData.Offset.Ticks > 0 ? thisOffsetData.Offset : new TimeSpan(0);
            start = new TimeSpan(0);
            TimeSpan timeToAddBetweenNextClips = nextOffsetData.FirstOccurrence() //"Supposed" start of next clip
                .Subtract(thisOffsetData.LastOccurrence()) //Minus end of this clip is how much time is now between
                .Subtract( //minus
                    nextOffsetData.Offset.Subtract(thisOffsetData.Offset) //How much time should be between
                ); //is how much needs to be added
            TimeSpan end = thisOffsetData.LastOccurrence().Add(new TimeSpan(timeToAddBetweenNextClips.Ticks / 2));
            length = end.Subtract(start);
            if (length.Ticks < 0)
            {
                throw  new OffsetException(useableOffsets[0]);
            }
#if DEBUG || PRINT_OFFSET_INFO
            Console.WriteLine($"{thisOffsetData.Offset:g} Starting at {start} ending at {end} occuring {thisOffsetData.Occurrences} times with confidence {thisOffsetData.Confidence * 100:F1}%");
#endif
            StartsAndLengths.Add(new StartAndLength {start = start, length = length});
            demuxFile.AddRange(MakeFfmpegDemuxInput(videoFile, start, end, length));
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
                //Save last End for comparisons
                TimeSpan lastEnd = end;
                start = thisOffsetData.FirstOccurrence().Subtract(new TimeSpan(timeToAddBetweenLastClip.Ticks / 2)); // move start back by half of what needs to be added back
                end = thisOffsetData.LastOccurrence().Add(timeToAddBetweenNextClips); // move end forward by half of what needs to be added back
                length = end.Subtract(start);
                // Length can't be negative for a section of video
                if (length.Ticks < 0)
                {
                    throw new OffsetException(useableOffsets[i]);
                }
                // Last section must have ended before this one begins
                if (lastEnd > start)
                {
                    throw new OffsetException(useableOffsets[i=1], useableOffsets[i]);
                }
#if DEBUG || PRINT_OFFSET_INFO
                Console.WriteLine($"{thisOffsetData.Offset:g} Starting at {start} ending at {end} occuring {thisOffsetData.Occurrences} times with confidence {thisOffsetData.Confidence * 100:F1}%");
#endif
                StartsAndLengths.Add(new StartAndLength {start = start, length = length});
                demuxFile.AddRange(MakeFfmpegDemuxInput(videoFile, start, end, length));
                timeToAddBetweenLastClip = timeToAddBetweenNextClips;
            }
            //last clip
            OffsetData lastOffsetData = useableOffsets[useableOffsets.Count - 1];
            start = lastOffsetData.FirstOccurrence().Subtract(new TimeSpan(timeToAddBetweenLastClip.Ticks / 2));
            end = lastOffsetData.LastOccurrence().Add(new TimeSpan(30*TimeSpan.TicksPerSecond + useableOffsets[0].Offset.Ticks));
            length = end.Subtract(start);
#if DEBUG || PRINT_OFFSET_INFO
            Console.WriteLine($"{lastOffsetData.Offset:g} Starting at {start} ending at {lastOffsetData.LastOccurrence()} occuring {lastOffsetData.Occurrences} times with confidence {lastOffsetData.Confidence * 100:F1}%");
#endif
            StartsAndLengths.Add(new StartAndLength {start = start, length = length});
            demuxFile.AddRange(MakeFfmpegDemuxInput(videoFile, start, end, length));
            return firstOffset;
        }

        private static string[] MakeFfmpegDemuxInput(string videoFile, TimeSpan start, TimeSpan end, TimeSpan length)
        {
            return new[] { $"file {videoFile}", $"inpoint {start}", $"duration {length}", $"outpoint {end}", "" };
        }
    }
}