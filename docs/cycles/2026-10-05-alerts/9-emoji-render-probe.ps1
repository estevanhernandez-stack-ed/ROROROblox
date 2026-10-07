# What does WPF actually draw for an emoji in a game title today?
#
# Item 9 asks whether to take a dependency for COLOUR emoji. The cost of NOT taking it depends
# entirely on what WPF renders now: missing glyphs (tofu boxes) would be a real defect, while
# monochrome outlines are merely less pretty. Measured rather than remembered.
#
# Method: render a TextBlock to a bitmap and count distinct colours. Monochrome/greyscale glyph
# outlines yield colours that all sit on the grey axis; a COLR-rendered emoji yields saturated hues.

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

$samples = @(
    @{ Name = "plain title";        Text = "Pet Simulator 99!" },
    @{ Name = "title with emoji";   Text = "Pet Simulator 99! 🐾" },
    @{ Name = "emoji-heavy title";  Text = "🎃 Halloween Update! 🍬" },
    @{ Name = "emoji only";         Text = "🐾" }
)

foreach ($s in $samples) {
    $tb = New-Object System.Windows.Controls.TextBlock
    $tb.Text = $s.Text
    $tb.FontSize = 24
    $tb.Foreground = [System.Windows.Media.Brushes]::White
    $tb.Background = [System.Windows.Media.Brushes]::Black
    $tb.Measure((New-Object System.Windows.Size([double]::PositiveInfinity, [double]::PositiveInfinity)))
    $size = $tb.DesiredSize
    $tb.Arrange((New-Object System.Windows.Rect(0, 0, $size.Width, $size.Height)))
    $tb.UpdateLayout()

    $w = [int][math]::Ceiling($size.Width); $h = [int][math]::Ceiling($size.Height)
    if ($w -le 0 -or $h -le 0) { "  $($s.Name): measured to nothing"; continue }

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($w, $h, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($tb)

    $stride = $w * 4
    $pixels = New-Object byte[] ($stride * $h)
    $rtb.CopyPixels([System.Windows.Int32Rect]::Empty, $pixels, $stride, 0)

    $distinct = @{}
    $saturated = 0
    for ($i = 0; $i -lt $pixels.Length; $i += 4) {
        $b = $pixels[$i]; $g = $pixels[$i+1]; $r = $pixels[$i+2]
        $distinct["$r,$g,$b"] = $true
        # Saturation proxy: how far the channels spread. Grey outlines keep this near zero.
        $spread = ([math]::Max($r, [math]::Max($g, $b)) - [math]::Min($r, [math]::Min($g, $b)))
        if ($spread -gt 40) { $saturated++ }
    }
    $pct = [math]::Round(100 * $saturated / ($w * $h), 2)
    "  {0,-20} {1,4}x{2,-4} distinct colours {3,5}   saturated pixels {4,6}%" -f $s.Name, $w, $h, $distinct.Count, $pct
}

"`nglyph coverage check (is the emoji even present in the font WPF picks?)"
$tf = New-Object System.Windows.Media.Typeface("Segoe UI Emoji")
$gt = $null
if ($tf.TryGetGlyphTypeface([ref]$gt)) {
    foreach ($cp in @(0x1F43E, 0x1F383, 0x1F36C)) {
        $has = $gt.CharacterToGlyphMap.ContainsKey($cp)
        "  U+{0:X5} present in Segoe UI Emoji: {1}" -f $cp, $has
    }
    "  font: $($gt.FamilyNames.Values -join ', ')  version $($gt.Version)"
} else {
    "  could not load Segoe UI Emoji glyph typeface"
}
