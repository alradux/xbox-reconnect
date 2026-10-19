using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
internal static class DpiLayoutTests {
    internal static bool Run(string outputDirectory) {
        Directory.CreateDirectory(outputDirectory); var report=new StringBuilder(); bool success=true;
        foreach(float scale in new[]{1f,1.25f,1.5f,1.75f,2f}) {
            using(var form=new ReconnectForm(true,scale)) {
                form.Show(); Application.DoEvents();
                string problem=form.CheckTextLayout(); success=success && problem==null;
                report.AppendLine((scale*100).ToString("0")+"%: "+(problem??"PASS: all label text fits; no label escapes its panel."));
                form.SetDpiTestContent(); problem=form.CheckTextLayout();
                if(problem!=null) { success=false; report.AppendLine("Long status text: "+problem); }
                using(var bitmap=new Bitmap(form.Width,form.Height)) {
                    form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
                    bitmap.Save(Path.Combine(outputDirectory,"dpi-"+(scale*100).ToString("0")+".png"));
                }
                // Smaller work areas must keep the full layout scrollable.
                form.ClientSize=new Size(800,600); Application.DoEvents();
                var viewport=form.Controls[0] as ScrollableControl;
                if(viewport==null || !viewport.VerticalScroll.Visible || (scale>=1.25f && !viewport.HorizontalScroll.Visible)) {
                    success=false; report.AppendLine("FAIL: content is not scrollable in an 800 x 600 client area. viewport "+viewport.ClientSize+"; display "+viewport.DisplayRectangle+"; content "+viewport.Controls[0].Bounds+"; horizontal "+viewport.HorizontalScroll.Visible+"; vertical "+viewport.VerticalScroll.Visible);
                }
                problem=form.CheckTextLayout(); if(problem!=null) { success=false; report.AppendLine("800 x 600: "+problem); }
            }
        }
        File.WriteAllText(Path.Combine(outputDirectory,"layout-checks.txt"),report.ToString()); return success;
    }
}
