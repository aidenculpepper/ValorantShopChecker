using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PersonalShop {
    static class Brand {
        public static void Draw(Graphics g,RectangleF bounds) {
            var state=g.Save(); g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TranslateTransform(bounds.X,bounds.Y); g.ScaleTransform(bounds.Width/100f,bounds.Height/100f);
            using(var path=new GraphicsPath()) {
                path.AddArc(1,1,36,36,180,90); path.AddArc(63,1,36,36,270,90); path.AddArc(63,63,36,36,0,90); path.AddArc(1,63,36,36,90,90); path.CloseFigure();
                using(var fill=new LinearGradientBrush(new RectangleF(0,0,100,100),Color.FromArgb(213,255,177),Color.FromArgb(154,221,123),65f)) g.FillPath(fill,path);
                using(var border=new Pen(Color.FromArgb(230,255,209),1.2f)) g.DrawPath(border,path);
            }
            // One continuous geometric N, shared by the vector logo and every icon size.
            PointF[] mark={new PointF(24,73),new PointF(24,27),new PointF(37,27),new PointF(63,53),new PointF(63,27),new PointF(76,27),new PointF(76,73),new PointF(63,73),new PointF(37,47),new PointF(37,73)};
            using(var fill=new SolidBrush(Color.FromArgb(15,29,23))) g.FillPolygon(fill,mark);
            g.Restore(state);
        }
    }
}
