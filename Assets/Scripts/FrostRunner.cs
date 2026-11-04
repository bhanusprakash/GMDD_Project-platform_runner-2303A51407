using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Frost Runner - single-script Unity port of the web game.
// Attach this to an empty GameObject in an empty scene and press Play.
// All sprites, sounds and UI are generated at runtime, so no assets are needed.
public class FrostRunner : MonoBehaviour
{
    const float W = 800f, H = 400f, S = 50f;   // game width/height in px, px per Unity unit
    const float PW = 26f, PH = 34f;            // player size
    enum State { Ready, Play, Over }

    class Plat  { public float x, y, w; }
    class Spike { public float x, y, w = 26; }
    class Coin  { public float x, y; public bool got, big; }
    class Foe   { public int type; public float x, y, w, h, o, d = 1, by, py; public Plat pl; public bool on, dead; } // 0 snowman, 1 ice shard, 2 icicle
    class Part  { public float x, y, vx, vy; public int l; public Color c; }
    class Flake { public float x, y, r, s; }

    // ---- colours ----
    static readonly Color Ice = new Color32(127, 208, 245, 255), IceTop = new Color32(233, 251, 255, 255), Stripe = new Color32(79, 179, 224, 255);
    static readonly Color MFar = new Color32(143, 196, 227, 255), MNear = new Color32(214, 238, 251, 255);
    static readonly Color SkyTop = new Color32(11, 42, 74, 255), SkyMid = new Color32(59, 130, 184, 255), SkyBot = new Color32(191, 230, 247, 255);
    static readonly Color Gold = new Color32(251, 191, 36, 255), Brown = new Color32(180, 83, 9, 255), LGold = new Color32(253, 230, 138, 255);
    static readonly Color Gem = new Color32(56, 189, 248, 255), Red = new Color32(239, 68, 68, 255), Blue = new Color32(37, 99, 235, 255);
    static readonly Color Navy = new Color32(15, 42, 107, 255), Navy2 = new Color32(30, 58, 138, 255), ArmB = new Color32(30, 64, 175, 255), ArmF = new Color32(59, 130, 246, 255);
    static readonly Color Orange = new Color32(249, 115, 22, 255), Fur = new Color32(241, 245, 249, 255), Face = new Color32(255, 224, 194, 255);
    static readonly Color Edge = new Color32(88, 182, 230, 255), Frost = new Color32(244, 253, 255, 255);

    // ---- state ----
    State state = State.Ready;
    float score, speed = 4f, sx, bgx, shake, acc, overAt, lastEnd;
    int t, best, coinCount;
    bool paused, sfx = true;
    float px, py, pvy; int pj; bool pg;
    List<Plat> plats = new List<Plat>(); List<Spike> spikes = new List<Spike>();
    List<Coin> coins = new List<Coin>(); List<Foe> foes = new List<Foe>();
    List<Part> parts = new List<Part>(); List<Flake> flakes = new List<Flake>();

    // ---- rendering ----
    Sprite sqr, circ, tri, triDown, dia;
    List<SpriteRenderer> pool = new List<SpriteRenderer>(); int used;
    Transform root; Camera cam; AudioSource src;
    Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

    // ================= input =================
#if ENABLE_INPUT_SYSTEM
    static bool K(System.Func<Keyboard, bool> f) { var k = Keyboard.current; return k != null && f(k); }
    bool JumpPressed() => K(k => k.spaceKey.wasPressedThisFrame || k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame)
        || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);
    bool PausePressed() => K(k => k.pKey.wasPressedThisFrame || k.escapeKey.wasPressedThisFrame);
    bool MutePressed() => K(k => k.mKey.wasPressedThisFrame);
    bool Left() => K(k => k.leftArrowKey.isPressed || k.aKey.isPressed);
    bool Right() => K(k => k.rightArrowKey.isPressed || k.dKey.isPressed);
    bool Down() => K(k => k.downArrowKey.isPressed || k.sKey.isPressed);
#else
    bool JumpPressed() => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Return) || Input.GetMouseButtonDown(0);
    bool PausePressed() => Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape);
    bool MutePressed() => Input.GetKeyDown(KeyCode.M);
    bool Left() => Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A);
    bool Right() => Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D);
    bool Down() => Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S);
#endif

    // ================= setup =================
    static FrostRunner inst;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (inst == null) new GameObject("Frost Runner").AddComponent<FrostRunner>(); }

    Sprite MakeSprite(System.Func<float, float, bool> inside)
    {
        int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + .5f) / n * 2 - 1, v = (y + .5f) / n * 2 - 1;
                px[y * n + x] = inside(u, v) ? Color.white : new Color(1, 1, 1, 0);
            }
        tex.SetPixels(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f), n);
    }

    void Awake()
    {
        inst = this;
        Application.targetFrameRate = 60;
        root = new GameObject("Sprites").transform;
        sqr = MakeSprite((u, v) => true);
        circ = MakeSprite((u, v) => u * u + v * v <= 1);
        tri = MakeSprite((u, v) => Mathf.Abs(u) <= (1 - v) / 2);
        triDown = MakeSprite((u, v) => Mathf.Abs(u) <= (1 + v) / 2);
        dia = MakeSprite((u, v) => Mathf.Abs(u) + Mathf.Abs(v) <= 1);

        cam = Camera.main;
        if (cam == null) { var g = new GameObject("Main Camera"); g.tag = "MainCamera"; cam = g.AddComponent<Camera>(); g.AddComponent<AudioListener>(); }
        cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = SkyMid;
        src = gameObject.AddComponent<AudioSource>();
        best = PlayerPrefs.GetInt("frostBest", 0);
        for (int i = 0; i < 70; i++) flakes.Add(new Flake { x = Random.value * W, y = Random.value * H, r = 1 + Random.value * 2.2f, s = .3f + Random.value * .9f });
        ResetGame();
    }

    void ResetGame()
    {
        px = 110; py = 266; pvy = 0; pj = 0; pg = true;
        plats.Clear(); spikes.Clear(); coins.Clear(); foes.Clear(); parts.Clear();
        plats.Add(new Plat { x = 0, y = 300, w = 500 }); lastEnd = 500;
        speed = 4; score = 0; coinCount = 0; shake = 0; sx = 0;
        while (lastEnd < W + 300) Gen();
    }

    // ================= level generation =================
    void Gen()
    {
        var last = plats[plats.Count - 1];
        float gap = 50 + Random.value * 35 + speed * 4;
        float y = Mathf.Round(Mathf.Clamp(last.y + Random.value * 80 - 40, 210, 330));
        float w = Mathf.Round(190 + Random.value * 200), x = Mathf.Round(lastEnd + gap);
        var pl = new Plat { x = x, y = y, w = w }; plats.Add(pl); lastEnd = x + w;
        float r = Random.value, gx = x - gap;
        if (w > 220 && r < .25f) spikes.Add(new Spike { x = x + w * .3f + Random.value * w * .3f, y = y });
        if (w > 200 && r > .45f && r < .58f) foes.Add(new Foe { type = 0, pl = pl, o = w * .5f, w = 28, h = 30, x = x + w * .5f, y = y - 30 });
        if (r > .7f && r < .78f) foes.Add(new Foe { type = 1, x = x + w * .3f, by = y - 95 - Random.value * 25, y = y - 100, w = 30, h = 18 });
        if (w > 240 && r > .9f) foes.Add(new Foe { type = 2, x = x + w * .5f, y = 0, py = y, w = 16, h = 34 });
        if (Random.value < .85f)
            for (int i = 0; i < 5; i++) { float u = i / 4f; coins.Add(new Coin { x = gx + gap * u, y = last.y + (y - last.y) * u - 65 - Mathf.Sin(u * Mathf.PI) * 55 }); }
        if (Random.value < .5f)
            for (int i = 0; i < 4; i++) coins.Add(new Coin { x = x + 30 + i * 28, y = y - 28 });
        if (Random.value < .15f) coins.Add(new Coin { x = x + w / 2, y = y - 125, big = true });
    }

    // ================= game logic =================
    void Jump()
    {
        if (paused) { paused = false; return; }
        if (state == State.Ready) { ResetGame(); state = State.Play; return; }
        if (state == State.Over) { if (Time.unscaledTime - overAt > .4f) { ResetGame(); state = State.Play; } return; }
        if (pg) pj = 0;
        if (pj < 2)
        {
            Burst(px + PW / 2, py + PH, Color.white, 6); Beep(pj == 0 ? 520 : 680, .12f);
            pvy = pj == 0 ? -11.5f : -10.5f; pj++; pg = false;
        }
    }

    void Die()
    {
        if (state == State.Over) return;
        state = State.Over; overAt = Time.unscaledTime; shake = 8;
        Burst(px + 13, py + 17, Ice, 24); Beep(150, .35f, false);
        int sc = Mathf.FloorToInt(score);
        if (sc > best) { best = sc; PlayerPrefs.SetInt("frostBest", best); PlayerPrefs.Save(); }
    }

    void Step()
    {
        t++;
        speed = Mathf.Min(8f, 4f + score / 830f);
        score += speed / 30f;
        sx += speed; int dx = Mathf.FloorToInt(sx); sx -= dx; bgx += speed;
        foreach (var o in plats) o.x -= dx; foreach (var o in spikes) o.x -= dx;
        foreach (var o in coins) o.x -= dx; foreach (var o in foes) o.x -= dx;
        lastEnd -= dx;
        plats.RemoveAll(o => o.x + o.w <= -60); spikes.RemoveAll(o => o.x <= -60); coins.RemoveAll(o => o.x <= -60);
        foes.RemoveAll(o => o.dead || o.x <= -80 || (o.pl != null && o.pl.x + o.pl.w < -60));
        while (lastEnd < W + 300) Gen();

        // enemies
        foreach (var f in foes)
        {
            if (f.dead) continue;
            if (f.type == 0) { f.o += f.d * .9f; if (f.o < 8 || f.o > f.pl.w - f.w - 8) f.d *= -1; f.x = f.pl.x + f.o; f.y = f.pl.y - f.h; }
            else if (f.type == 1) { f.x -= 1; f.y = f.by + Mathf.Sin(t / 12f + f.by) * 14; }
            else { if (!f.on && f.x < px + 240) f.on = true; if (f.on) { f.y += 5; if (f.y + f.h >= f.py) f.dead = true; } }
            if (px + PW - 8 > f.x + 5 && px + 8 < f.x + f.w - 5 && py + PH - 5 > f.y + 5 && py + 5 < f.y + f.h - 5)
            {
                if (f.type == 0 && pvy > 0 && py + PH <= f.y + 16)
                { f.dead = true; pvy = -9; pj = 1; score += 17; Burst(f.x + 14, f.y, Color.white, 10); Beep(300, .12f); }
                else Die();
            }
        }

        // player movement
        if (Left()) px -= 4; if (Right()) px += 4;
        px = Mathf.Clamp(px, 20, W - 220);
        if (Down() && !pg) pvy = Mathf.Min(pvy + 1.2f, 16);
        float prev = py + PH;
        pvy += .7f; py += pvy; pg = false;
        foreach (var pl in plats)
            if (pvy >= 0 && px + PW > pl.x + 4 && px < pl.x + pl.w - 4 && prev <= pl.y + 1 && py + PH >= pl.y)
            { py = pl.y - PH; pvy = 0; pg = true; pj = 0; }
        foreach (var s in spikes)
            if (px + PW - 9 > s.x + 6 && px + 9 < s.x + s.w - 6 && py + PH > s.y - 14 && py < s.y) Die();
        foreach (var c in coins)
            if (!c.got && Vector2.Distance(new Vector2(px + PW / 2, py + PH / 2), new Vector2(c.x, c.y)) < (c.big ? 34 : 30))
            {
                c.got = true; coinCount += c.big ? 5 : 1; score += c.big ? 33 : 8;
                Burst(c.x, c.y, c.big ? Gem : Gold, 8); Beep(c.big ? 1100 : 880, .1f, false);
            }
        coins.RemoveAll(c => c.got);
        if (py > H + 40) Die();
    }

    void Burst(float x, float y, Color col, int n)
    {
        for (int i = 0; i < n; i++)
            parts.Add(new Part { x = x, y = y, vx = (Random.value - .5f) * 4, vy = (Random.value - .8f) * 4, l = 25 + Random.Range(0, 15), c = col });
    }

    void Beep(float f, float d, bool square = true, float vol = .12f)
    {
        if (!sfx || src == null) return;
        string key = f + "_" + d + "_" + square;
        if (!clips.TryGetValue(key, out var clip))
        {
            int n = (int)(44100 * d); var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float ph = i * f / 44100f % 1f;
                float w = square ? (ph < .5f ? 1f : -1f) : Mathf.Sin(ph * Mathf.PI * 2);
                float env = 1f - (float)i / n; data[i] = w * vol * env * env;
            }
            clip = AudioClip.Create(key, n, 1, 44100, false); clip.SetData(data, 0); clips[key] = clip;
        }
        src.PlayOneShot(clip);
    }

    // ================= main loop =================
    void Update()
    {
        if (PausePressed() && state == State.Play) paused = !paused;
        if (MutePressed()) sfx = !sfx;
        if (JumpPressed()) Jump();

        acc += Mathf.Min(.1f, Time.unscaledDeltaTime);
        while (acc >= .015f)
        {
            if (state == State.Play && !paused) Step(); else t++;
            foreach (var q in parts) { q.x += q.vx; q.y += q.vy; q.vy += .15f; q.l--; }
            parts.RemoveAll(q => q.l <= 0);
            foreach (var f in flakes)
            {
                f.y += f.s; f.x -= f.s * 1.5f + (state == State.Play ? speed * .3f : 0);
                if (f.y > H) f.y = -4; if (f.x < 0) f.x = W;
            }
            if (shake > 0) shake -= .4f;
            acc -= 1f / 60f;
        }
    }

    void OnApplicationFocus(bool focus) { if (!focus && state == State.Play) paused = true; }

    void LateUpdate()
    {
        used = 0; Render();
        for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
    }

    // ================= drawing =================
    SpriteRenderer Draw(Sprite s, Color c, float cx, float cy, float w, float h, int order, float rot = 0)
    {
        if (used == pool.Count)
        {
            var go = new GameObject("s"); go.transform.SetParent(root);
            pool.Add(go.AddComponent<SpriteRenderer>());
        }
        var sr = pool[used++];
        sr.enabled = true; sr.sprite = s; sr.color = c; sr.sortingOrder = order;
        var tr = sr.transform;
        tr.position = new Vector3((cx - W / 2) / S, (H / 2 - cy) / S, 0);
        tr.localScale = new Vector3(w / S, h / S, 1);
        tr.rotation = Quaternion.Euler(0, 0, -rot * Mathf.Rad2Deg);
        return sr;
    }
    // rectangle given by its top-left corner (game pixels, y down)
    void Box(Color c, float x, float y, float w, float h, int order, float rot = 0) => Draw(sqr, c, x + w / 2, y + h / 2, w, h, order, rot);

    void Render()
    {
        cam.orthographicSize = Mathf.Max(4f, 8f / cam.aspect);
        cam.transform.position = new Vector3(shake > 0 ? (Random.value - .5f) * shake / S : 0, shake > 0 ? (Random.value - .5f) * shake / S : 0, -10);

        // sky
        for (int i = 0; i < 12; i++)
        {
            float u = i / 11f;
            Color c = u < .6f ? Color.Lerp(SkyTop, SkyMid, u / .6f) : Color.Lerp(SkyMid, SkyBot, (u - .6f) / .4f);
            Draw(sqr, c, W / 2, (i + .5f) * H / 12, W * 4, H / 12 + 1, 0);
        }
        // mountains
        for (float xx = 0; xx <= W; xx += 10)
        {
            float a = 250 - Mathf.Abs(Mathf.Sin((xx + bgx * .1f) / 130f)) * 90;
            float b = 300 - Mathf.Abs(Mathf.Sin((xx + bgx * .25f) / 90f + 1)) * 60;
            Box(MFar, xx, a, 10, H - a, 1); Box(MNear, xx, b, 10, H - b, 2);
        }
        // platforms
        foreach (var pl in plats)
        {
            Box(Ice, pl.x, pl.y, pl.w, H - pl.y, 3);
            Box(IceTop, pl.x, pl.y, pl.w, 8, 4);
            for (float xx = pl.x + 10; xx < pl.x + pl.w - 10; xx += 34) Box(Stripe, xx, pl.y + 14, 3, Mathf.Min(40, H - pl.y - 20), 4);
        }
        // icicle spikes
        foreach (var s in spikes)
        {
            Draw(tri, Edge, s.x + 13, s.y - 14, 30, 32, 5);
            Draw(tri, Frost, s.x + 13, s.y - 13, 24, 28, 6);
        }
        // coins and gems
        float spin = Mathf.Abs(Mathf.Cos(t / 30f)) * .8f + .2f;
        foreach (var c in coins)
        {
            if (c.big) { Draw(dia, Color.white, c.x, c.y, 30, 38, 7); Draw(dia, Gem, c.x, c.y, 26, 34, 8); }
            else { Draw(circ, Brown, c.x, c.y, 24 * spin, 24, 7); Draw(circ, Gold, c.x, c.y, 20 * spin, 20, 8); Draw(circ, LGold, c.x, c.y, 9 * spin, 9, 9); }
        }
        // enemies
        foreach (var f in foes)
        {
            if (f.dead) continue;
            if (f.type == 0)
            {
                Draw(circ, Color.white, f.x + 14, f.y + 22, 18, 18, 8); Draw(circ, Color.white, f.x + 14, f.y + 9, 14, 14, 8);
                Box(Color.black, f.x + 11, f.y + 7, 2, 2, 9); Box(Color.black, f.x + 16, f.y + 7, 2, 2, 9); Box(Color.black, f.x + 8, f.y - 3, 12, 5, 9);
                Box(Orange, f.x + 14, f.y + 10, 8, 2, 9);
            }
            else if (f.type == 1)
            {
                Draw(dia, Edge, f.x + f.w / 2, f.y + f.h / 2, f.w + 3, f.h + 3, 8); Draw(dia, new Color32(199, 240, 255, 255), f.x + f.w / 2, f.y + f.h / 2, f.w, f.h, 9);
                Box(Red, f.x + 6, f.y + 6, 3, 3, 10);
            }
            else { Draw(triDown, Edge, f.x + f.w / 2, f.y + f.h / 2, f.w + 3, f.h + 3, 8); Draw(triDown, Frost, f.x + f.w / 2, f.y + f.h / 2, f.w, f.h, 9); }
        }
        DrawHero();
        // snow
        foreach (var f in flakes) Draw(circ, new Color(1, 1, 1, .85f), f.x, f.y, f.r * 2, f.r * 2, 18);
        // particles
        foreach (var q in parts) { var c = q.c; c.a = Mathf.Clamp01(q.l / 30f); Box(c, q.x - 2, q.y - 2, 4, 4, 20); }
    }

    void DrawHero()
    {
        float x = Mathf.Round(px), y = Mathf.Round(py), cx = x + 13;
        bool dead = state == State.Over, air = !pg, up = air && pvy < 0, still = dead || paused;
        float ph = t / 3.4f, sw = air || still ? 0 : Mathf.Sin(ph), bob = air || still ? 0 : -Mathf.Abs(Mathf.Cos(ph)) * 2;
        float fa = (air && pj >= 2 && !dead) ? Mathf.Clamp01((pvy + 10.5f) / 14.5f) * Mathf.PI * 2 : 0; // double-jump flip
        float ox = cx, oy = y + 17;
        float cs = Mathf.Cos(fa), sn = Mathf.Sin(fa);

        // draws a sprite at a local position, rotated around the body pivot during the flip
        void P(Sprite s, Color c, float lx, float ly, float w, float h, int order, float rot = 0)
        {
            float dx = lx - ox, dy = ly + bob - oy;
            Draw(s, c, ox + dx * cs - dy * sn, oy + dx * sn + dy * cs, w, h, order, rot + fa);
        }
        void Arm(float d, Color col, int order)
        {
            float a = air ? (up ? (d > 0 ? 2.4f : -2.4f) : (d > 0 ? 1.5f : -1.5f)) : d * sw;
            float shx = cx + 1, shy = y + 19, hx = shx + Mathf.Sin(a) * 9, hy = shy + Mathf.Cos(a) * 9;
            P(sqr, col, (shx + hx) / 2, (shy + hy) / 2, 5, 10, order, -a);
            P(circ, Orange, hx, hy, 6.5f, 6.5f, order);
        }

        // scarf tail
        float L = 16 + speed * 2, fl = up ? -5 : air ? 3 : 0;
        for (int i = 0; i < 7; i++)
        {
            float u = i / 6f;
            P(sqr, Red, cx - 4 - u * L, y + 19.5f + Mathf.Sin(t / 4f - u * 5) * 3 * u + fl * u, L / 6 + 1.5f, 5, 21);
        }
        Arm(-1, ArmB, 21);
        // legs
        foreach (float d in new[] { -1f, 1f })
        {
            float lx = cx - 5 + d * sw * 6, len = 7 - Mathf.Max(0, -d * Mathf.Sin(ph)) * (air || still ? 0 : 3);
            if (air) { lx = cx - 5 + d * (up ? 3 : 5); len = up ? 4 : 7; }
            P(sqr, d < 0 ? Navy2 : Navy, lx + 5, y + 27 + len / 2, 10, len, 22);
        }
        // body
        P(sqr, Blue, x + 13, y + 22.5f, 22, 15, 23);
        P(sqr, Navy, x + 13, y + 24.5f, 22, 3, 24);
        P(sqr, Fur, cx + 5, y + 19, 2, 8, 24);
        Arm(1, ArmF, 25);
        P(sqr, Red, cx + 2, y + 17.5f, 16, 5, 26);
        // head
        P(circ, Fur, cx + 1, y + 9, 25, 25, 27);
        P(circ, Face, cx + 3, y + 10, 17, 17, 28);
        P(circ, new Color(.96f, .45f, .71f, .55f), cx + 8, y + 13, 4, 4, 29);
        float ey = y + 9 + (up ? -1 : air ? 1 : 0);
        if (dead) { P(sqr, Color.black, cx + 6, ey, 6, 1.5f, 30, .785f); P(sqr, Color.black, cx + 6, ey, 6, 1.5f, 30, -.785f); }
        else if (t % 170 < 7) P(sqr, Color.black, cx + 6, ey + .75f, 4, 1.5f, 30);
        else P(circ, Color.black, cx + 6, ey, 4, 4, 30);
    }

    // ================= UI (IMGUI, works with either input system) =================
    void OnGUI()
    {
        float k = Screen.height / 400f;
        var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(18 * k), fontStyle = FontStyle.Bold };
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(16 * k, 6 * k, 400 * k, 30 * k), "Distance " + Mathf.FloorToInt(score) + " m", st);
        GUI.Label(new Rect(16 * k, 30 * k, 400 * k, 30 * k), "Coins " + coinCount, st);
        st.alignment = TextAnchor.UpperRight;
        GUI.Label(new Rect(Screen.width - 416 * k, 6 * k, 400 * k, 30 * k), "Best " + best, st);

        if (state != State.Play || paused)
        {
            GUI.color = new Color(.02f, .12f, .2f, .6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            st.alignment = TextAnchor.MiddleCenter; st.fontSize = Mathf.RoundToInt(44 * k);
            GUI.Label(new Rect(0, Screen.height / 2f - 70 * k, Screen.width, 60 * k), paused ? "Paused" : state == State.Ready ? "Frost Runner" : "You Froze!", st);
            st.fontSize = Mathf.RoundToInt(20 * k);
            if (state == State.Over && !paused)
                GUI.Label(new Rect(0, Screen.height / 2f - 8 * k, Screen.width, 30 * k), "Distance " + Mathf.FloorToInt(score) + " m   |   Coins " + coinCount, st);
            GUI.Label(new Rect(0, Screen.height / 2f + 26 * k, Screen.width, 30 * k),
                paused ? "Press P, Space or tap to resume" : state == State.Ready ? "Press Space or tap to start" : "Press Space or tap to try again", st);
        }
    }
}
