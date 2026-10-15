using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Timeline? selectionOwner;
    SpriteSelectionState? selectionState;
    public bool DrawConfiguredJacket(Session session,double time)
    {
        if(session.NativeGimmick.Behavior("multiply-jacket") is not { } component)return false;
        Prepare(session);
        string mod=component.Mod("frame");string sprite;int frame=0,index=0;
        if(component.SpriteLists.TryGetValue("variants",out var variants))
        {
            if(!ReferenceEquals(selectionOwner,session.Timeline))
            {selectionOwner=session.Timeline;selectionState=new(session.Timeline,mod,variants.Length);}
            index=selectionState!.At(time);
            if(index==(int)component.Number("skipIndex",-1))return true;
            sprite=variants[index];
        }
        else
        {
            sprite=component.Sprite("atlas");
            if(current==null||!current.Sprites.TryGetValue(sprite,out var info))return false;
            double value=session.Timeline.Get(mod,time);
            if(!double.IsFinite(value))return false;
            frame=(int)((Math.Floor(value)%info.Frames.Count+info.Frames.Count)%info.Frames.Count);
        }
        if(Image(sprite,frame,linear:component.Flag("linear",true)) is { } texture)
        {
            canvas.Blend(BlendFactor.DestinationColor,BlendFactor.Zero);
            canvas.Quad(texture,new((float)component.Number("x",0),(float)component.Number("y",0),
                (float)component.Number("width",320),(float)component.Number("height",180)),Color.White);
            canvas.Blend(BlendFactor.SourceAlpha,BlendFactor.InverseSourceAlpha);canvas.Flush();
        }
        return true;
    }
}
