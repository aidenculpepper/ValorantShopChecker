using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PersonalShop {
    static class Style {
        public static readonly Color Bg=Color.FromArgb(12,15,19), Panel=Color.FromArgb(21,25,30), Line=Color.FromArgb(40,47,53), Text=Color.FromArgb(241,244,237), Muted=Color.FromArgb(130,143,147), Mint=Color.FromArgb(190,249,150), Orange=Color.FromArgb(255,155,106);
        public static GraphicsPath Round(RectangleF r,float radius) { float d=radius*2; var p=new GraphicsPath(); p.AddArc(r.X,r.Y,d,d,180,90); p.AddArc(r.Right-d,r.Y,d,d,270,90); p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.X,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p; }
        public static void Box(Graphics g,RectangleF r,Color fill,Color border,float radius) { using(var p=Round(r,radius)) { using(var b=new SolidBrush(fill)) g.FillPath(b,p); if(border.A>0) using(var pen=new Pen(border)) g.DrawPath(pen,p); } }
        public static void TextAt(Graphics g,string text,float size,Color color,Rectangle r,bool bold=false,TextFormatFlags flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis) { using(var f=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular)) TextRenderer.DrawText(g,text,f,r,color,flags|TextFormatFlags.NoPadding); }
    }
    class VaultButton : Button {
        bool hover; public bool Accent, Selected; public string Small="";
        public VaultButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); UseVisualStyleBackColor=false; FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; BackColor=Style.Bg; ForeColor=Style.Text; Font=new Font("Segoe UI",10,FontStyle.Bold); Cursor=Cursors.Hand; DoubleBuffered=true; }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.Clear(BackColor); g.SmoothingMode=SmoothingMode.AntiAlias;
            Color bg=Accent?(hover?Color.FromArgb(211,255,178):Style.Mint):(Selected?Color.FromArgb(37,47,35):(hover?Color.FromArgb(34,40,46):Style.Panel));
            Style.Box(g,new RectangleF(0,0,Width-1,Height-1),bg,Accent?Color.Transparent:(Selected?Color.FromArgb(77,108,60):Style.Line),8);
            Style.TextAt(g,Text,10,Enabled?(Accent?Style.Bg:(Selected?Style.Mint:Style.Text)):Style.Muted,new Rectangle(8,0,Width-16,Height),true,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
    }
    class PaintedPanel : Panel {
        public PaintedPanel() { DoubleBuffered=true; BackColor=Style.Bg; }
    }
    partial class ShopWindow : Form {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
        VaultButton refresh, demo, regions, dailyTab, nightTab, closeButton, minButton, maxButton, settings;
        Preferences preferences=Preferences.Load(); ReleaseInfo available; bool updateBusy, settingsOpen, autoPending; string updateStatus="Not checked yet";
        readonly System.Windows.Forms.Timer updateTimer=new System.Windows.Forms.Timer();
        PaintedPanel content; Label status; string selectedRegion="auto", stamp="PERSONAL STOREFRONT", mode="OFFLINE", clock="-- : -- : --";
        ShopData data; bool busy, closing, preview; int selectedTab; DateTime lastRequest=DateTime.MinValue; CancellationTokenSource activeRefresh;
        readonly System.Windows.Forms.Timer tick=new System.Windows.Forms.Timer();
        int pulse; readonly ToolTip tips=new ToolTip();
        public ShopWindow(bool capture) {
            preview=capture; Text="Nightshift · Valorant Shop Checker "+Updates.VersionText; Font=new Font("Segoe UI",10); ForeColor=Style.Text; BackColor=Style.Bg; FormBorderStyle=FormBorderStyle.None;
            using(var icon=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Nightshift.ico")) if(icon!=null) Icon=new Icon(icon);
            ClientSize=new Size(1240,850); MinimumSize=new Size(1050,760); StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Dpi; DoubleBuffered=true;
            regions=Button("Auto-detect  ▾",false); regions.Width=150;
            var menu=new ContextMenuStrip { BackColor=Style.Panel,ForeColor=Style.Text,ShowImageMargin=false };
            string[] names={"Auto-detect","Americas","Europe","Asia Pacific","Korea","PBE"},keys={"auto","na","eu","ap","kr","pbe"};
            selectedRegion=preferences.Region; regions.Text=names[Array.IndexOf(keys,selectedRegion)]+"  ▾";
            for(int i=0;i<names.Length;i++) { string name=names[i],key=keys[i]; var item=new ToolStripMenuItem(name); item.Click+=delegate { selectedRegion=key; regions.Text=name+"  ▾"; preferences.Region=key; SavePreferences(); }; menu.Items.Add(item); }
            regions.Click+=delegate { menu.Show(regions,new Point(0,regions.Height)); };
            refresh=Button("↻  Refresh shop",true); refresh.Width=166; refresh.Click+=async delegate { await RefreshShop(); };
            demo=Button("Preview",false); demo.Width=88; demo.Click+=delegate { ShowDemo(); };
            settings=new NavigationButton {Gear=true,Size=new Size(44,44),AccessibleName="Settings"}; Controls.Add(settings); tips.SetToolTip(settings,"Settings and updates"); settings.Click+=delegate { OpenSettings(); };
            shopNavigation=new NavigationButton {Size=new Size(44,46),Selected=true,AccessibleName="Shop"}; Controls.Add(shopNavigation); tips.SetToolTip(shopNavigation,"Back to your shop"); shopNavigation.Click+=delegate { CloseSettings(); };
            dailyTab=Button("Daily offers",false); dailyTab.Width=145; dailyTab.Selected=true; dailyTab.Click+=delegate { SwitchTab(0); };
            nightTab=Button("Night Market",false); nightTab.Width=145; nightTab.Click+=delegate { SwitchTab(1); };
            closeButton=Button("×",false); closeButton.Size=new Size(34,26); closeButton.Click+=delegate { Close(); };
            minButton=Button("−",false); minButton.Size=new Size(34,26); minButton.Click+=delegate { WindowState=FormWindowState.Minimized; };
            maxButton=Button("□",false); maxButton.Size=new Size(34,26); maxButton.Click+=delegate { WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized; };
            tips.SetToolTip(refresh,"Fetch the latest offers. Any pre-existing Riot Client stays open."); tips.SetToolTip(demo,"View clearly labelled sample offers");
            content=new PaintedPanel { AutoScroll=true }; Controls.Add(content);
            status=new Label { BackColor=Style.Bg,ForeColor=Style.Muted,Font=new Font("Segoe UI",9),AutoEllipsis=true,Text="Ready. Enable Stay signed in in Riot Client, then refresh." }; Controls.Add(status);
            Resize+=delegate { Arrange(); }; MouseDown+=Drag;
            tick.Interval=50; tick.Tick+=async delegate { pulse=(pulse+1)%120; if(busy) Invalidate(new Rectangle(84,45,Width-84,270)); if(pulse%20==0) { UpdateClock(); RefreshSettingsState(); if(autoPending && !preview && !busy && !updateBusy && !settingsOpen && !closing) { autoPending=false; if(preferences.AutomaticUpdates && preferences.AutomaticInstall) await InstallUpdate(true); } } }; tick.Start();
            FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(busy) { e.Cancel=true; closing=true; activeRefresh.Cancel(); status.Text="Finishing cleanup before closing..."; } };
            updateTimer.Interval=21600000; updateTimer.Tick+=async delegate { if(preferences.AutomaticUpdates) await CheckUpdate(); }; if(!capture) updateTimer.Start();
            FormClosed+=delegate { tick.Dispose(); updateTimer.Dispose(); tips.Dispose(); menu.Dispose(); DisposeData(data); };
            Shown+=async delegate { if(preview) ShowDemo(); else { if(preferences.RefreshOnLaunch) await RefreshShop(); if(preferences.AutomaticUpdates) await CheckUpdate(); } };
            Arrange();
        }
        VaultButton Button(string text,bool accent) { var b=new VaultButton {Text=text,Accent=accent,Height=40}; Controls.Add(b); return b; }
        void Drag(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left && e.Y<58) { ReleaseCapture(); SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } }
        protected override void WndProc(ref Message m) {
            base.WndProc(ref m);
            if(m.Msg==0x84 && WindowState==FormWindowState.Normal) {
                int xy=m.LParam.ToInt32(); Point p=PointToClient(new Point((short)(xy&0xffff),(short)(xy>>16))); int edge=6;
                if(p.X>=Width-edge && p.Y>=Height-edge) m.Result=new IntPtr(17);
                else if(p.X<edge && p.Y>=Height-edge) m.Result=new IntPtr(16);
                else if(p.Y>=Height-edge) m.Result=new IntPtr(15);
                else if(p.X>=Width-edge) m.Result=new IntPtr(11);
                else if(p.X<edge) m.Result=new IntPtr(10);
            }
        }
        void Arrange() {
            if(content==null) return;
            closeButton.Location=new Point(Width-47,13); maxButton.Location=new Point(Width-87,13); minButton.Location=new Point(Width-127,13);
            settings.Location=new Point(20,Height-72);
            shopNavigation.Location=new Point(20,104);
            if(settingsPage!=null) settingsPage.Bounds=new Rectangle(96,79,Width-112,Height-145);
            refresh.Location=new Point(Width-198,332); regions.Location=new Point(Width-362,332); demo.Location=new Point(Width-464,332);
            dailyTab.Location=new Point(116,332); nightTab.Location=new Point(271,332);
            content.Bounds=new Rectangle(108,425,Width-128,Height-491);
            status.Bounds=new Rectangle(135,Height-41,Width-420,23);
            LayoutCards(); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var p=new Pen(Style.Line)) { g.DrawRectangle(p,0,0,Width-1,Height-1); g.DrawLine(p,84,0,84,Height); g.DrawLine(p,84,56,Width,56); g.DrawLine(p,108,Height-54,Width-20,Height-54); }
            Style.Box(g,new RectangleF(19,17,46,43),Style.Mint,Color.Transparent,12);
            using(var b=new SolidBrush(Style.Bg)) g.FillPolygon(b,new Point[]{new Point(28,29),new Point(35,29),new Point(43,43),new Point(51,29),new Point(57,29),new Point(45,50),new Point(40,50)});
            Style.TextAt(g,"NIGHTSHIFT",11,Style.Text,new Rectangle(116,14,180,27),true);
            Style.Box(g,new RectangleF(20,104,44,46),Color.FromArgb(32,43,30),Color.FromArgb(61,81,46),10);
            using(var pen=new Pen(Style.Mint,2)) { g.DrawRectangle(pen,31,116,22,20); g.DrawLine(pen,31,122,53,122); g.DrawLine(pen,38,116,38,136); }
            Style.TextAt(g,"SHOP",7,settingsOpen?Style.Muted:Style.Mint,new Rectangle(16,158,52,20),true,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            if(settingsOpen) return;
            int hx=116,hy=79,hw=Width-148,hh=231;
            using(var p=Style.Round(new RectangleF(hx,hy,hw,hh),16)) using(var b=new LinearGradientBrush(new Rectangle(hx,hy,hw,hh),Color.FromArgb(31,43,34),Color.FromArgb(23,28,30),15f)) g.FillPath(b,p);
            var saved=g.Save(); using(var clip=Style.Round(new RectangleF(hx,hy,hw,hh),16)) g.SetClip(clip);
            int cx=hx+(int)(hw*0.40),cy=hy+115;
            using(var pen=new Pen(Color.FromArgb(25,Style.Mint))) {
                for(int r=70;r<240;r+=40) g.DrawEllipse(pen,cx-r,cy-r,r*2,r*2);
                for(int x=hx+hw/2;x<hx+hw;x+=36) g.DrawLine(pen,x,hy,x,hy+hh);
            }
            using(var b=new SolidBrush(Color.FromArgb(12,Style.Mint))) g.FillPolygon(b,new Point[]{new Point(Width-660,hy+hh),new Point(Width-440,hy),new Point(Width-260,hy),new Point(Width-480,hy+hh)});
            var featured=data!=null && data.Daily.Count>0 && data.Daily[0].Art!=null?data.Daily[0].Art:null;
            if(featured!=null) {
                var art=featured; float scale=Math.Min((Width<1150?450f:580f)/art.Width,170f/art.Height); int w=(int)(art.Width*scale),h=(int)(art.Height*scale); g.DrawImage(art,cx-w/2,cy-h/2+18,w,h);
            }
            g.Restore(saved);
            int bx=Width-250;
            Style.Box(g,new RectangleF(bx,hy+21,191,77),Color.FromArgb(17,23,22),Color.FromArgb(51,68,48),10);
            Style.TextAt(g,"NEXT ROTATION",7,Style.Muted,new Rectangle(bx+15,hy+31,160,20),true);
            Style.TextAt(g,clock,20,Style.Mint,new Rectangle(bx+13,hy+52,164,32),true);
            Style.Box(g,new RectangleF(bx,hy+175,191,30),Style.Bg,Style.Line,15);
            using(var b=new SolidBrush(mode=="LIVE"?Style.Mint:Style.Orange)) g.FillEllipse(b,bx+12,hy+186,7,7);
            Style.TextAt(g,busy?"SYNCING YOUR SHOP":mode=="LIVE"?"LIVE SHOP  /  "+data.Shard.ToUpperInvariant():mode=="PREVIEW"?"PREVIEW  /  SAMPLE DATA":"READY TO CONNECT",7,Style.Text,new Rectangle(bx+27,hy+178,158,23),true);
            if(busy) { int barW=(hw-30)/4,travel=(hw-barW-30)*pulse/119; using(var b=new SolidBrush(Style.Mint)) g.FillRectangle(b,hx+15+travel,hy+hh-3,barW,2); }
            Style.TextAt(g,selectedTab==0?"TODAY'S SELECTION":"AFTER HOURS",16,Style.Text,new Rectangle(116,390,380,25),true);
            string count=data==null?"":selectedTab==0?data.Daily.Count+" PERSONAL OFFERS":data.Night.Count+" DISCOUNTED OFFERS";
            Style.TextAt(g,count,8,Style.Muted,new Rectangle(Width-395,394,360,23),false,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            using(var b=new SolidBrush(busy?Style.Orange:(mode=="LIVE"?Style.Mint:Style.Muted))) g.FillEllipse(b,117,Height-33,6,6);
            Style.TextAt(g,"Made by AC",7,Style.Muted,new Rectangle(Width-272,Height-39,236,20),false,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
        }
        public void SwitchTab(int tab) { selectedTab=tab; dailyTab.Selected=tab==0; nightTab.Selected=tab==1; dailyTab.Invalidate(); nightTab.Invalidate(); content.AutoScrollPosition=Point.Empty; LayoutCards(); Invalidate(); }
        static void DisposeData(ShopData d) { if(d==null) return; foreach(var o in d.Daily) if(o.Art!=null) o.Art.Dispose(); foreach(var o in d.Night) if(o.Art!=null) o.Art.Dispose(); }
        void ClearContent() { while(content.Controls.Count>0) content.Controls[0].Dispose(); }
        void LayoutCards() {
            if(content==null || settingsOpen) return; content.SuspendLayout(); ClearContent();
            var offers=data==null?null:(selectedTab==0?data.Daily:data.Night);
            int count=offers==null?4:offers.Count;
            if(count==0) {
                var empty=new EmptyMarket {Location=new Point(8,5),Size=new Size(content.ClientSize.Width-26,285)}; content.Controls.Add(empty); content.ResumeLayout(); return;
            }
            int columns=selectedTab==1?3:4; int gap=14,available=content.ClientSize.Width-28,cardW=(available-gap*(columns-1))/columns,cardH=selectedTab==1?265:Math.Min(315,Math.Max(245,content.ClientSize.Height-15));
            for(int i=0;i<count;i++) {
                var offer=offers==null?null:offers[i]; var card=new OfferCard(offer,i,selectedTab==1) {Location=new Point(8+(i%columns)*(cardW+gap),5+(i/columns)*(cardH+gap)),Size=new Size(cardW,cardH)};
                if(offer!=null) card.Click+=delegate { using(var detail=new OfferDetail(offer)) detail.ShowDialog(this); };
                content.Controls.Add(card);
            }
            content.ResumeLayout();
        }
        async Task RefreshShop() {
            if(busy || updateBusy) return; if((DateTime.UtcNow-lastRequest).TotalSeconds<15) { status.Text="Give Riot a moment. Refresh again in a few seconds."; return; }
            busy=true; lastRequest=DateTime.UtcNow; refresh.Enabled=false; demo.Enabled=false; regions.Enabled=false; refresh.Text="Syncing...";
            activeRefresh=new CancellationTokenSource(); var report=new Progress<string>(message=> { if(!IsDisposed && !closing && busy) status.Text=message; });
            DisposeData(data); data=null; mode="OFFLINE"; stamp="CONNECTING TO RIOT"; clock="-- : -- : --"; status.ForeColor=Style.Muted; status.Text="Loading your current offers..."; LayoutCards(); Invalidate(); string region=selectedRegion;
            try {
                ShopData result=await Task.Run(()=>Api.Load(region,message=>((IProgress<string>)report).Report(message),activeRefresh.Token));
                if(IsDisposed || closing) { DisposeData(result); return; } ShowLive(result);
            } catch(Exception ex) { if(!IsDisposed) { status.ForeColor=Style.Orange; status.Text=ex is FriendlyException?ex.Message:"Could not load the shop. Restart Riot Client and try again."; stamp="PERSONAL STOREFRONT"; } }
            finally { busy=false; activeRefresh.Dispose(); if(!IsDisposed) { refresh.Enabled=true; demo.Enabled=true; regions.Enabled=true; refresh.Text="↻  Refresh shop"; Invalidate(); if(closing) Close(); } }
        }
        public void ShowDemo() {
            DisposeData(data); data=new ShopData {Expires=DateTime.UtcNow.AddHours(8).AddMinutes(42),NightExpires=DateTime.UtcNow.AddDays(2)};
            string[] names={"Prime Vandal","Reaver Sheriff","Oni Phantom","Recon Balisong"}; int[] prices={1775,1775,1775,3550};
            for(int i=0;i<4;i++) data.Daily.Add(new Offer {Name=names[i],Price=prices[i],Id="sample"});
            for(int i=0;i<6;i++) data.Night.Add(new Offer {Name=names[i%4],Price=1065+i*50,Id="sample",Discount=30+i*3});
            mode="PREVIEW"; stamp="PREVIEW  /  SAMPLE OFFERS"; status.ForeColor=Style.Muted; status.Text="Sample offers only. Refresh to reveal your real shop."; LayoutCards(); UpdateClock(); Invalidate();
        }
        public void ShowLive(ShopData result) {
            DisposeData(data); data=result; mode="LIVE"; stamp="YOUR SHOP  /  UPDATED "+DateTime.Now.ToString("h:mm tt").ToUpperInvariant();
            status.ForeColor=data.Warning==""?Style.Muted:Style.Orange; status.Text=data.Warning==""?"Shop loaded. "+data.ClientNote:data.Warning; LayoutCards(); UpdateClock(); Invalidate();
        }
        void UpdateClock() { if(data==null) return; var t=data.Expires-DateTime.UtcNow; clock=t.TotalSeconds>0?((int)t.TotalHours).ToString("00")+" : "+t.Minutes.ToString("00")+" : "+t.Seconds.ToString("00"):"REFRESH NOW"; Invalidate(new Rectangle(Width-252,98,200,82)); }
    }
    class OfferCard : Control {
        readonly Offer offer; readonly int index; readonly bool night; bool hover;
        static readonly Color[] colors={Color.FromArgb(169,221,160),Color.FromArgb(183,160,227),Color.FromArgb(255,184,137),Color.FromArgb(143,195,220)};
        public OfferCard(Offer o,int i,bool n) { offer=o; index=i; night=n; DoubleBuffered=true; BackColor=Style.Bg; Cursor=o==null?Cursors.Default:Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; g.InterpolationMode=InterpolationMode.HighQualityBicubic; var tone=colors[index%4];
            var rect=new RectangleF(1,1,Width-3,Height-3); Style.Box(g,rect,Style.Panel,hover?tone:Style.Line,12);
            var state=g.Save(); using(var path=Style.Round(rect,12)) g.SetClip(path);
            int artH=Height-103;
            using(var b=new LinearGradientBrush(new Rectangle(0,0,Width,artH),Color.FromArgb(hover?56:38,tone.R/2,tone.G/2,tone.B/2),Style.Panel,90f)) g.FillRectangle(b,0,0,Width,artH);
            using(var pen=new Pen(Color.FromArgb(20,tone))) { for(int j=-Height;j<Width;j+=40) g.DrawLine(pen,j,artH,j+artH,0); g.DrawEllipse(pen,Width/2-62,artH/2-54,124,124); g.DrawEllipse(pen,Width/2-82,artH/2-74,164,164); }
            Style.TextAt(g,(index+1).ToString("00"),9,tone,new Rectangle(16,14,50,23),true);
            if(night && offer!=null) { Style.Box(g,new RectangleF(Width-68,14,51,25),Color.FromArgb(46,36,31),Color.Transparent,6); Style.TextAt(g,"−"+offer.Discount+"%",9,Style.Orange,new Rectangle(Width-64,14,43,25),true,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter); }
            else Style.TextAt(g,offer==null?"LOCKED":"↗",12,Style.Muted,new Rectangle(Width-76,14,59,25),false,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            if(offer!=null && offer.Art!=null) {
                float scale=Math.Min((Width-25f)/offer.Art.Width,(artH-63f)/offer.Art.Height)*(hover?1.04f:1f); int w=(int)(offer.Art.Width*scale),h=(int)(offer.Art.Height*scale); g.DrawImage(offer.Art,(Width-w)/2,51+(artH-65-h)/2,w,h);
            } else {
                using(var pen=new Pen(Color.FromArgb(80,tone),2)) { int cx=Width/2,cy=artH/2+12; g.DrawEllipse(pen,cx-19,cy-19,38,38); g.DrawLine(pen,cx-31,cy,cx+31,cy); g.DrawLine(pen,cx,cy-31,cx,cy+31); }
                Style.TextAt(g,offer==null?"WAITING FOR YOUR DROP":"ARTWORK UNAVAILABLE",7,Style.Muted,new Rectangle(12,artH-30,Width-24,20),false,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            }
            using(var pen=new Pen(Color.FromArgb(65,tone))) g.DrawLine(pen,17,artH,Width-17,artH);
            g.Restore(state);
            string name=offer==null?"Your next upgrade":offer.Name=="Uncatalogued skin"?"Skin "+offer.Id.Substring(0,Math.Min(8,offer.Id.Length)):offer.Name;
            Style.TextAt(g,name,12,offer==null?Style.Muted:Style.Text,new Rectangle(17,artH+13,Width-34,40),true,TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
            Style.TextAt(g,offer==null?"REFRESH TO REVEAL":night?"NIGHT MARKET":"DAILY ROTATION",7,Style.Muted,new Rectangle(17,Height-33,Width-34,20),true);
            string price=offer==null?"—":offer.Price.HasValue?offer.Price.Value.ToString("N0")+" VP":"— VP";
            Style.TextAt(g,price,11,tone,new Rectangle(Width-115,Height-37,96,24),true,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
        }
    }
    class EmptyMarket : Control {
        public EmptyMarket() { DoubleBuffered=true; }
        protected override void OnPaint(PaintEventArgs e) {
            var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; Style.Box(g,new RectangleF(1,1,Width-3,Height-3),Style.Panel,Style.Line,12);
            Style.TextAt(g,"THE NIGHT IS STILL YOUNG.",23,Style.Text,new Rectangle(35,50,Width-70,55),true);
            Style.TextAt(g,"No Night Market offers are available right now.\nCheck back when Riot opens the next market.",12,Style.Muted,new Rectangle(37,120,Width-74,70),false,TextFormatFlags.Left|TextFormatFlags.WordBreak);
        }
    }
    class OfferDetail : Form {
        Offer offer;
        public OfferDetail(Offer o) {
            offer=o; Text=o.Name; ClientSize=new Size(680,450); BackColor=Style.Bg; ForeColor=Style.Text; StartPosition=FormStartPosition.CenterParent; FormBorderStyle=FormBorderStyle.None; MaximizeBox=false; MinimizeBox=false; DoubleBuffered=true; ShowInTaskbar=false;
            var close=new VaultButton {Text="Back to shop",Accent=true,Location=new Point(490,380),Size=new Size(155,40)}; close.Click+=delegate { Close(); }; Controls.Add(close); AcceptButton=close; CancelButton=close;
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias; using(var pen=new Pen(Style.Line)) g.DrawRectangle(pen,0,0,Width-1,Height-1); Style.TextAt(g,offer.Discount>0?"NIGHT MARKET  /  −"+offer.Discount+"%":"YOUR DAILY OFFER",9,Style.Mint,new Rectangle(34,24,600,30),true);
            if(offer.Art!=null) { float scale=Math.Min(595f/offer.Art.Width,220f/offer.Art.Height); int w=(int)(offer.Art.Width*scale),h=(int)(offer.Art.Height*scale); g.DrawImage(offer.Art,(Width-w)/2,78+(220-h)/2,w,h); }
            Style.TextAt(g,offer.Name,22,Style.Text,new Rectangle(34,304,610,65),true,TextFormatFlags.Left|TextFormatFlags.WordBreak);
            Style.TextAt(g,offer.Price.HasValue?offer.Price.Value.ToString("N0")+" VP":"Price unavailable",16,Style.Mint,new Rectangle(34,380,400,40),true);
        }
    }
}

