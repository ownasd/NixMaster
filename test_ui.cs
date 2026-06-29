using System;
using System.Drawing;
using System.Windows.Forms;

class Program {
    [STAThread]
    static void Main() {
        var form = new Form { Width = 800, Height = 600 };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        
        var card = new Panel {
            BackColor = Color.LightGray,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(400, 0),
            MaximumSize = new Size(400, 0)
        };
        
        var hdr = new Panel { Dock = DockStyle.Top, Height = 50, BackColor = Color.DarkGray };
        card.Controls.Add(hdr);
        
        var row1 = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Color.Red };
        card.Controls.Add(row1);
        
        flow.Controls.Add(card);
        form.Controls.Add(flow);
        
        Console.WriteLine($"Card Width: {card.Width}, Height: {card.Height}");
    }
}
