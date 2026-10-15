namespace KuroakiGimmick.Core;

/// <summary>Presentation PCM pauses the original song during automatic interludes and resumes the exact next sample.</summary>
public static class NativeInterludeAudio
{
    public static AudioData? Render(AudioData? song, NativeInterludeRoute? route, NativeGimmickProfile profile, double songDuration)
    {
        if (route == null || !route.HasChanges) return song;
        int rate = song?.Rate ?? 48000, channels = song?.Channels ?? 2;
        double duration = route.PresentationAt(songDuration, true);
        var samples = new float[AudioData.ValidateSampleCount(checked((long)Math.Ceiling(duration * rate) * channels))];
        int sourceFrames = (song?.Samples.Length ?? 0) / channels;
        foreach (var leg in route.Legs.Where(leg=>!leg.Hold))
        {
            int sourceStart=(int)Math.Clamp(Math.Round(leg.SongStart*rate),0,sourceFrames);
            int sourceEnd=(int)Math.Clamp(Math.Round(Math.Min(songDuration,leg.SongEnd)*rate),0,sourceFrames);
            int outputStart=(int)Math.Clamp(Math.Round(leg.PresentationStart*rate),0,samples.Length/channels);
            int frames = Math.Max(0, Math.Min(sourceEnd - sourceStart, samples.Length / channels - outputStart));
            if (song != null && frames > 0) Array.Copy(song.Samples, sourceStart * channels, samples, outputStart * channels, frames * channels);
        }
        foreach(var seek in route.Seeks.Where(seek=>seek.FadeInSeconds>0))
        {
            int first=(int)Math.Clamp(Math.Ceiling(seek.PresentationTime*rate),0,samples.Length/channels);
            int last=(int)Math.Clamp(Math.Ceiling((seek.PresentationTime+seek.FadeInSeconds)*rate),0,samples.Length/channels);
            for(int frame=first;frame<last;frame++)
            {
                float gain=(float)Math.Clamp((frame/(double)rate-seek.PresentationTime)/seek.FadeInSeconds,0,1);
                for(int channel=0;channel<channels;channel++)samples[frame*channels+channel]*=gain;
            }
        }
        var decoded = new Dictionary<string,AudioData>(StringComparer.Ordinal);
        foreach (var entry in route.Entries)
        {
            var actions = entry.Schedule.Actions;
            foreach (var timed in actions.Where(a => a.Action.Kind is "bgm" or "sound"))
            {
                var action = timed.Action;
                if (action.Path == null || !profile.Resources.TryGetValue("interlude-audio/" + action.Path, out string? file)) continue;
                try
                {
                    if (!decoded.TryGetValue(file,out var audio)) decoded[file] = audio = AudioData.Decode(file);
                    string id=action.Id??(action.Kind=="bgm"?"bgm":"");
                    bool Matches(NativeInterludeAction other) => id.Length>0&&(other.Id??"bgm")==id;
                    var fades = actions.Where(a => a.Time >= timed.Time && a.Action.Kind == "bgm-fade" && Matches(a.Action))
                        .Select(a=>(Time:entry.PresentationStart+a.Time,To:a.Action.To,Duration:a.Action.Duration)).ToArray();
                    var next = actions.FirstOrDefault(a => a.Time > timed.Time && a.Action.Kind is "bgm" or "sound" && Matches(a.Action));
                    var ended=actions.FirstOrDefault(a=>a.Time>=timed.Time&&a.Action.Kind=="audio-stop"&&Matches(a.Action));
                    var pitches=actions.Where(a=>a.Time>=timed.Time&&a.Action.Kind=="audio-pitch"&&Matches(a.Action))
                        .Select(a=>(Time:entry.PresentationStart+a.Time,Rate:a.Action.To)).ToArray();
                    double lowestPitch=pitches.Select(p=>p.Rate).Append(1).Min();
                    double stop = action.Loop ? duration-entry.PresentationStart : timed.Time + audio.Duration/Math.Max(.01,lowestPitch);
                    if (next != null) stop = Math.Min(stop,next.Time);
                    if(ended!=null)stop=Math.Min(stop,ended.Time);
                    if (fades.Length>0&&fades[^1].To==0) stop=Math.Min(stop,fades[^1].Time-entry.PresentationStart+fades[^1].Duration);
                    Mix(samples,rate,channels,audio,entry.PresentationStart+timed.Time,Math.Max(0,stop-timed.Time),action.Gain,action.Loop,
                        fades,pitches);
                }
                catch (Exception ex) when (ResourceFiles.IsResourceError(ex) || ex is System.ComponentModel.Win32Exception)
                { profile.Fail("interlude-audio/"+action.Path,ex.Message); }
            }
        }
        for (int i=0;i<samples.Length;i++) samples[i]=Math.Clamp(samples[i],-1,1);
        return new(samples,rate,channels);
    }

    static void Mix(float[] output,int rate,int channels,AudioData clip,double start,double duration,double gain,bool loop,
        IReadOnlyList<(double Time,double To,double Duration)> fades,
        IReadOnlyList<(double Time,double Rate)> pitches)
    {
        if(start>=output.Length/(double)(rate*channels))return;
        int startFrame=(int)Math.Round(start*rate),sourceFrames=clip.Samples.Length/clip.Channels;
        if(sourceFrames==0)return;
        int count=(int)Math.Clamp(Math.Ceiling(duration*rate),0,output.Length/channels-startFrame);
        float Sample(int frame,int channel)
        {
            frame=loop?frame%sourceFrames:Math.Min(frame,sourceFrames-1);
            return channels==1&&clip.Channels==2?(clip.Samples[frame*2]+clip.Samples[frame*2+1])*.5f:
                clip.Samples[frame*clip.Channels+Math.Min(channel,clip.Channels-1)];
        }
        double sourcePosition=0,pitch=1;
        int pitchIndex=0,fadeIndex=0;
        double gainFrom=gain,gainTo=gain,gainStart=start,gainDuration=0;
        double Level(double at)=>gainDuration<=0?gainTo:gainFrom+(gainTo-gainFrom)*Math.Clamp((at-gainStart)/gainDuration,0,1);
        for(int frame=0;frame<count;frame++)
        {
            double at=start+frame/(double)rate;
            while(pitchIndex<pitches.Count&&pitches[pitchIndex].Time<=at)pitch=pitches[pitchIndex++].Rate;
            while(fadeIndex<fades.Count&&fades[fadeIndex].Time<=at)
            {
                var fade=fades[fadeIndex++];
                gainFrom=Level(fade.Time);gainTo=fade.To;gainStart=fade.Time;gainDuration=fade.Duration;
            }
            if(!loop&&sourcePosition>=sourceFrames)break;
            int index=(int)Math.Floor(sourcePosition);double blend=sourcePosition-index;
            double level=Level(at);
            for(int channel=0;channel<channels;channel++)
                output[(startFrame+frame)*channels+channel]+=(float)((Sample(index,channel)*(1-blend)+Sample(index+1,channel)*blend)*level);
            sourcePosition+=clip.Rate/(double)rate*pitch;
            if(loop)sourcePosition%=sourceFrames;
        }
    }
}
