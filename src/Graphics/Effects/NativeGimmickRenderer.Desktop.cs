using System.Numerics;
using KuroakiGimmick.Core;

namespace KuroakiGimmick.Graphics;

public sealed partial class NativeGimmickRenderer
{
    Target? desktopFiltered,desktopTitle;
    Session? desktopTitleOwner;
    public bool RenderDesktopComposite(Target output,Texture input,Session session,double time,GameUiRenderer? text)
    {
        Prepare(session);
        if(current?.Data?.Desktop is not { } spec)return false;
        desktopFiltered??=new(canvas.Gpu,input.Width,input.Height);
        if(desktopFiltered.Texture.Width!=input.Width||desktopFiltered.Texture.Height!=input.Height)
        {canvas.Flush();desktopFiltered.Resize(input.Width,input.Height);}
        if(spec.WindowShader==null||!ApplyShader(desktopFiltered,input,session,time,spec.WindowShader))
            canvas.Pass(desktopFiltered,input,canvas.Basic);
        desktopTitle??=new(canvas.Gpu,324,196);
        if(!ReferenceEquals(desktopTitleOwner,session))
        {
            desktopTitleOwner=session;
            canvas.Begin(desktopTitle,324,196,324,196,new(0,0,0,0));
            if(spec.WindowTitles.Length>0)
            {
                int index=Math.Min(spec.WindowTitles.Length-1,(int)(Timeline.Hash(8266)*spec.WindowTitles.Length));
                text?.DrawDesktopTitle(session,spec.WindowTitles[index]);
            }
            canvas.Flush();
        }
        canvas.Begin(output,output.Texture.Width,output.Texture.Height,320,180,Color.Hex(0));
        if(Image(spec.Desktop) is { } bg)canvas.Quad(bg,new(0,0,320,180),Color.White);
        double beat=session.Timeline.Bpm.Beat(time);
        if(beat>276 && beat<279 && spec.Cameo is { } cameo && Image(cameo) is { } cameoImage)
            canvas.Quad(cameoImage,new(172,73,30,35),Color.White);
        text?.DrawDesktopClock(session,time);
        foreach(var window in NativeDesktopState.Windows(session.Timeline,time))
        {
            var transform=Matrix3x2.CreateTranslation(-160,-90)*Matrix3x2.CreateScale((float)window.ScaleX,(float)window.ScaleY)
                *Matrix3x2.CreateRotation((float)(-window.Angle*Math.PI/180))*Matrix3x2.CreateTranslation((float)window.X,(float)window.Y);
            void Quad(Texture image,Rect rect,Color color)
            {
                canvas.Polygon(image,Vector2.Transform(new(rect.X,rect.Y),transform),Vector2.Transform(new(rect.X+rect.W,rect.Y),transform),
                    Vector2.Transform(new(rect.X,rect.Y+rect.H),transform),Vector2.Transform(new(rect.X+rect.W,rect.Y+rect.H),transform),color);
            }
            Quad(canvas.White,new(0,0,320,180),Color.Hex(0));
            Quad(desktopFiltered.Texture,new(0,0,320,180),Color.White);
            if(Image(spec.WindowFrame) is { } frame)Quad(frame,new(-2,-14,324,196),Color.White);
            Quad(canvas.White,new(0,0,320,180),Color.White.Alpha(session.Timeline.Get("wflash",time)));
            Quad(desktopTitle.Texture,new(-2,-14,324,196),Color.White);
        }
        double alpha=session.Timeline.Get("static",time);
        if(alpha>0 && spec.Static is { } snow)
        {
            int frame=(int)((Math.Floor(beat*16)%4+4)%4),i=0;
            foreach(var (x,y) in new[]{(0,0),(160,0),(160,90),(0,90)})
                if(Image(snow,(frame+i++)%4) is { } image)canvas.Quad(image,new(x,y,160,90),Color.White.Alpha(alpha));
        }
        canvas.Flush();return true;
    }
    void DisposeDesktopTargets(){desktopFiltered?.Dispose();desktopTitle?.Dispose();desktopFiltered=desktopTitle=null;desktopTitleOwner=null;}
}
