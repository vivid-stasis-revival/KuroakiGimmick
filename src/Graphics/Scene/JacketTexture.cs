using StbImageSharp;

namespace KuroakiGimmick.Graphics;

/// <summary>分块保存封面原图；每次仅绘制与动画当前帧相交的区域。</summary>
internal sealed class JacketTexture : IDisposable
{
    private readonly List<(Texture Texture, int X, int Y, int Width, int Height, int Left, int Top)> tiles = [];
    private readonly int width, height;
    public int Width => width;
    public int Height => height;

    public JacketTexture(GpuDevice gpu, string path, bool linear)
    {
        using var stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        width = image.Width;
        height = image.Height;
        const int limit = 4096;
        try
        {
            if (width <= limit && height <= limit)
            {
                tiles.Add((new Texture(gpu,width,height,image.Data,linear:linear),0,0,width,height,0,0));
                return;
            }
            // 邻块各借一列或一行像素，使线性采样在分块边缘也能读取原图的相邻颜色。
            for (int y = 0; y < height; y += limit - 2)
            for (int x = 0; x < width; x += limit - 2)
            {
                int w = Math.Min(limit - 2,width-x), h = Math.Min(limit - 2,height-y);
                int left = Math.Max(0,x-1), top = Math.Max(0,y-1);
                int tw = Math.Min(width,x+w+1)-left, th = Math.Min(height,y+h+1)-top;
                var pixels = new byte[checked(tw*th*4)];
                for (int row = 0; row < th; row++)
                    Buffer.BlockCopy(image.Data, ((row+top)*width+left)*4, pixels, row*tw*4, tw*4);
                tiles.Add((new Texture(gpu,tw,th,pixels,linear:linear),x,y,w,h,x-left,y-top));
            }
        }
        catch { Dispose(); throw; }
    }

    public void Draw(Canvas canvas, Rect target, Color tint, int frame, int frames)
    {
        double frameWidth = (double)width/frames, start = frameWidth*frame;
        foreach (var tile in tiles)
        {
            double a = Math.Max(start,tile.X), b = Math.Min(start+frameWidth,tile.X+tile.Width);
            if (b <= a) continue;
            canvas.Quad(tile.Texture,new(target.X+(float)((a-start)/frameWidth)*target.W,
                target.Y+target.H*tile.Y/height,(float)((b-a)/frameWidth)*target.W,target.H*tile.Height/height),tint,
                new((float)((tile.Left+a-tile.X)/tile.Texture.Width),(float)tile.Top/tile.Texture.Height,
                    (float)((b-a)/tile.Texture.Width),(float)tile.Height/tile.Texture.Height));
        }
    }

    public void Dispose()
    {
        foreach (var tile in tiles) tile.Texture.Dispose();
        tiles.Clear();
    }
}
