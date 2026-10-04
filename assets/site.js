// Foldspace site: language switch (繁體中文 / English), latest-release info from GitHub, screenshots.
(() => {
  "use strict";

  const REPO = "yueyang666/Foldspace";
  const LANGS = ["zh-Hant", "en"];

  // 繁體中文 is written in index.html; this is the English text for the same keys.
  const EN = {
    "title": "Foldspace — Drop it in the folder, it lands on the other PC",
    "description": "Foldspace opens a permanent door between two Windows PCs: pair once, then drag files onto “Foldspace” on your desktop and they arrive, complete, in the other PC's receive folder.",
    "skip": "Skip to main content",
    "nav.features": "Features",
    "nav.design": "Design",
    "nav.guide": "Guide",
    "nav.download": "Download",
    "nav.support": "Support",
    "hero.eyebrow": "File transfer for Windows on your local network",
    "hero.title": "Drop it in the folder.<br>It lands on the other PC.",
    "hero.lead": "Foldspace opens a permanent door between two Windows PCs. Pair them once, then drag a file or folder onto “Foldspace” on your desktop and it shows up, complete, in the other PC's receive folder — nobody has to click anything on the other side.",
    "cta.download": "Download for Windows",
    "cta.guide": "How to use",
    "hero.fine": "Windows 10 21H2 or later / Windows 11 · x64 · No installer",
    "demo.this": "This PC",
    "demo.other": "Other PC",
    "demo.file": "Trip photos",

    "features.kicker": "Why Foldspace",
    "features.title": "Two PCs, joined by one folder",
    "features.lead": "Made for your desktop and laptop, or two office PCs that trade files all day.",
    "f1.t": "Works like a folder",
    "f1.d": "“Foldspace” on the desktop looks like any other folder. Drop something on it and it's sent, with the folder structure intact.",
    "f2.t": "Finds the other PC",
    "f2.d": "Click “Pair…” to list the PCs on your network running Foldspace. If the other PC's IP changes, Foldspace finds it again by its device fingerprint.",
    "f3.t": "Pair once",
    "f3.d": "Both PCs show the same 6-digit code; confirm it and they remember each other. From then on each PC only trusts the other's certificate.",
    "f4.t": "Encrypted by default",
    "f4.d": "The control connection always uses TLS with mutual certificates. File data is encrypted by default, and you can turn that off for speed.",
    "f5.t": "No broken files",
    "f5.d": "Every file is hash-checked and resent if it doesn't match. If a transfer is cancelled or the network drops, no partial files are left in the receive folder.",
    "f6.t": "Light and native",
    "f6.d": "A single exe, no installer, living in the notification area. The UI uses native Windows controls and comes in English, Traditional Chinese and Simplified Chinese.",

    "how.kicker": "Three steps",
    "how.title": "Set up in a minute",
    "s1.t": "One copy on each PC",
    "s1.d": "Download Foldspace.exe and run it on both PCs. The first run puts “Foldspace” on the desktop and asks you to allow a firewall rule.",
    "s2.t": "Pair",
    "s2.d": "On either PC, click “Pair…” and pick the other one. Check that both show the same 6-digit code, and you're done.",
    "s3.t": "Drag and drop",
    "s3.d": "Drag files or folders onto “Foldspace” on the desktop. They arrive in Downloads\\Foldspace on the other PC.",

    "screens.kicker": "Screenshots",
    "screens.title": "Just the Windows you know",
    "screens.lead": "Settings, pairing and transfers are built from standard Windows controls, so everything works the way system tools do.",
    "shot.general": "Settings",
    "shot.general.d": "See at a glance whether you're connected; the everyday options are here too.",
    "shot.discovery": "Find the other PC",
    "shot.discovery.d": "PCs on your network running Foldspace are listed automatically. Just pick one.",
    "shot.code": "Compare the code",
    "shot.code.d": "Both PCs show the same 6 digits. Confirm they match and you're paired.",
    "shot.transfer": "Transfer progress",
    "shot.transfer.d": "Shows the speed and time left, and you can cancel at any time.",
    "shot.toast": "Notification",
    "shot.toast.d": "When everything has arrived, a Windows notification lets you know.",

    "design.kicker": "Design highlights",
    "design.title": "Simple on the outside, careful on the inside",
    "design.lead": "Foldspace does only a few things, and gets every detail right.",
    "d.a": "PC A",
    "d.b": "PC B",
    "d.udp": "UDP 52500 · discovery on the subnet",
    "d.ctrl": "Control channel · TLS, mutual certificates · heartbeat and pairing",
    "d.data": "Data channel · one per transfer, either direction · encrypted by default",
    "d.foot": "One TCP port (52500) plus UDP discovery on the same port; the firewall rule only admits the local subnet",
    "h1.t": "A pairing code that stops interception",
    "h1.d": "The code isn't derived from the two certificate fingerprints alone. The initiator first sends a <em>commitment</em> to a random value and only reveals it after the other side has published its own, so an attacker can't precompute a matching 6-digit code.",
    "h2.t": "Only complete files in the receive folder",
    "h2.d": "Files are written to a hidden staging area and checked with XxHash3 as each one finishes; a mismatch is resent once on the same connection. Only verified files are moved into the receive folder, with their modification times kept.",
    "h3.t": "Built for throughput",
    "h3.d": "Files are streamed in chunks without waiting between them; small files are read and written in parallel, and network reads are decoupled from disk writes. Sending a 5 GB file, Foldspace itself uses about 100 MB of memory.",
    "h4.t": "Trusts only the PC you paired",
    "h4.d": "After pairing, each side only accepts the other's certificate fingerprint, whatever its IP. Before pairing, only pairing requests are accepted and every transfer is refused; the firewall rule only opens the local subnet.",
    "h5.t": "Each PC speaks its own language",
    "h5.d": "Only status codes travel over the network, never text. An English PC and a Chinese PC each show messages in their own language.",
    "h6.t": "Disconnects and cancels, handled",
    "h6.d": "A heartbeat every 5 seconds; after 15 seconds of silence the peer is marked offline and Foldspace reconnects on its own. A deliberate cancel shows “Cancelled”, a dropped connection or full disk shows “Failed” — never mixed up.",

    "guide.kicker": "Guide",
    "guide.title": "From first run to sending files",
    "g1.t": "Installing and first run",
    "g1.1": "Download <code>Foldspace-&lt;version&gt;-win-x64.exe</code> on both PCs, put it in any folder you like and run it. There's no installer.",
    "g1.2": "The exe isn't code-signed, so Windows may show “Windows protected your PC”. Choose “More info” → “Run anyway”.",
    "g1.3": "On first run a UAC prompt appears. If you allow it, Foldspace adds a Windows Firewall rule that only lets PCs on the same subnet connect, on both private and public networks.",
    "g1.4": "“Foldspace” appears on the desktop and its icon sits in the notification area; the dot at its lower-left corner shows the connection state.",
    "g2.t": "Pairing two PCs",
    "g2.1": "On either PC, open the settings window and click “Pair…” on the General tab.",
    "g2.2": "The list shows the PCs on your network that are running Foldspace. Pick the other PC.",
    "g2.3": "Both PCs show a pairing code at the same time. If the 6 digits match, click “Confirm” on each; if they don't, click “Reject”.",
    "g2.4": "If the other PC doesn't show up (a different subnet, or guest Wi-Fi that blocks broadcasts), enter its IP address on the Connection tab.",
    "g3.t": "Sending and receiving",
    "g3.1": "Drag files or folders onto “Foldspace” on the desktop, or onto the “Send files” area in the settings window. For many items, drag their parent folder instead.",
    "g3.2": "The transfer window shows speed and time left, and you can cancel at any time. New transfers queue up, and both directions can run at once.",
    "g3.3": "Received files go to Downloads\\Foldspace by default. When they arrive a notification appears; click it to open the folder with the new item selected.",
    "g3.4": "On the Receive tab you can choose to rename, overwrite or skip files with the same name, and whether to ask before receiving.",
    "g4.t": "Reading the connection state",
    "st.green": "Connected: paired and ready to send",
    "st.yellow": "Searching: connecting or reconnecting",
    "st.orange": "Not paired: the other PC was found but isn't paired (or unpaired you)",
    "st.red": "Offline or error: no response, incompatible versions, pairing key mismatch, and so on",
    "st.gray": "Disabled, or not paired with any PC yet",
    "g5.t": "FAQ",
    "q1": "“Pair…” doesn't find the other PC?",
    "a1": "Discovery uses broadcasts on the local network, so both PCs must be on the same subnet. Guest Wi-Fi often isolates clients; enter the other PC's IP on the Connection tab instead.",
    "q2": "“Test” times out?",
    "a2": "Usually Foldspace isn't running on the other PC, its service is disabled, or a firewall is blocking it. On the Connection tab, “Allow Foldspace through Windows Firewall…” sets the rule up again.",
    "q3": "Lots of small files are slow?",
    "a3": "Windows Defender scans every new file, which can make ten thousand small files several times slower. Zipping them first is much faster.",
    "q4": "“Pairing key mismatch”?",
    "a4": "The other PC was reinstalled or its settings were deleted, so its device certificate changed. Unpair on both PCs and pair again.",
    "q5": "“Incompatible version”?",
    "a5": "The two PCs run Foldspace versions that are too far apart. Update both to the latest release; pairing is kept.",
    "g6.t": "Uninstalling",
    "g6.d": "Use Settings > Apps, or right-click “Foldspace” in the Start menu and choose “Uninstall”. The program, settings, pairing, logs and shortcuts are removed; files in the receive folder are kept. Removing the firewall rule needs administrator rights, so a UAC prompt appears.",

    "dl.kicker": "Download",
    "dl.title": "Download Foldspace",
    "dl.meta": "Windows 10 21H2 or later / Windows 11 · x64 · single exe",
    "dl.all": "All releases and notes →",
    "support.t": "Support Foldspace",
    "support.card.t": "Support Foldspace's development",
    "support.d": "Foldspace will always be free and open source. If you like it, your support helps make the Windows release more complete and more trustworthy, and brings Foldspace to more operating systems in the future.",
    "support.intro": "Your support makes the Windows release more complete and more trustworthy, and helps bring Foldspace to more operating systems. Thank you!",
    "nav.support.title": "Support Foldspace's development",
    "support.btn": "Support me on Ko-fi",
    "support.open": "Can't see the payment form? Open it on Ko-fi ↗",
    "dl.verify.t": "Verify the download",
    "dl.verify.d": "Every release comes with a .sha256 file. Run this in PowerShell and compare the result with the value in the .sha256 file:",
    "privacy.t": "Privacy",
    "privacy.d": "Foldspace only sends files to the PC you paired with. It doesn't connect to any server and doesn't collect or report any usage data. Settings, pairing data and logs stay on your own PC.",
    "footer.issues": "Report an issue",
  };

  const $$ = (sel) => Array.from(document.querySelectorAll(sel));
  const textEls = $$("[data-i18n]");
  const htmlEls = $$("[data-i18n-html]");
  const titleEls = $$("[data-i18n-title]");
  // Keep the original 繁體中文 so we can switch back without reloading.
  textEls.forEach((el) => { el.dataset.zh = el.textContent; });
  htmlEls.forEach((el) => { el.dataset.zh = el.innerHTML; });
  titleEls.forEach((el) => { el.dataset.zhTitle = el.title; });
  const zhTitle = document.title;
  const metaDesc = document.querySelector('meta[name="description"]');
  const zhDesc = metaDesc ? metaDesc.content : "";

  let lang = pickLanguage();
  let release = null;

  function pickLanguage() {
    const fromUrl = new URLSearchParams(location.search).get("lang");
    if (LANGS.includes(fromUrl)) return fromUrl;
    try {
      const saved = localStorage.getItem("foldspace-lang");
      if (LANGS.includes(saved)) return saved;
    } catch { /* storage may be blocked */ }
    const prefs = navigator.languages || [navigator.language || ""];
    return prefs.some((l) => /^zh\b/i.test(l)) ? "zh-Hant" : "en";
  }

  function applyLanguage() {
    const en = lang === "en";
    document.documentElement.lang = lang;
    textEls.forEach((el) => {
      const key = el.dataset.i18n;
      el.textContent = en && EN[key] !== undefined ? EN[key] : el.dataset.zh;
    });
    htmlEls.forEach((el) => {
      const key = el.dataset.i18nHtml;
      el.innerHTML = en && EN[key] !== undefined ? EN[key] : el.dataset.zh;
    });
    titleEls.forEach((el) => {
      const key = el.dataset.i18nTitle;
      el.title = en && EN[key] !== undefined ? EN[key] : el.dataset.zhTitle;
    });
    document.title = en ? EN.title : zhTitle;
    if (metaDesc) metaDesc.content = en ? EN.description : zhDesc;
    const toggle = document.getElementById("lang-toggle");
    if (toggle) {
      toggle.textContent = en ? "中文" : "EN";
      toggle.setAttribute("aria-label", en ? "切換成中文" : "Switch to English");
    }
    renderRelease();
    loadScreenshots();
    drawKofiWidget();
  }

  document.getElementById("lang-toggle")?.addEventListener("click", () => {
    lang = lang === "en" ? "zh-Hant" : "en";
    try { localStorage.setItem("foldspace-lang", lang); } catch { /* ignore */ }
    applyLanguage();
  });

  // ---------- Latest release ----------
  async function fetchRelease() {
    try {
      const res = await fetch(`https://api.github.com/repos/${REPO}/releases/latest`, {
        headers: { Accept: "application/vnd.github+json" },
      });
      if (!res.ok) return;
      const data = await res.json();
      const asset = (data.assets || []).find((a) => /-win-x64\.exe$/i.test(a.name));
      if (!asset) return;
      release = {
        version: String(data.tag_name || "").replace(/^v/, ""),
        name: asset.name,
        url: asset.browser_download_url,
        size: asset.size,
      };
      renderRelease();
    } catch { /* offline or rate-limited: keep the link to the Releases page */ }
  }

  function renderRelease() {
    if (!release) return;
    const mb = `${Math.round(release.size / 1048576)} MB`;
    $$(".js-download").forEach((a) => { a.href = release.url; });
    $$(".js-version").forEach((el) => { el.textContent = release.version; });
    const meta = document.querySelector(".js-dl-meta");
    if (meta) meta.textContent = `${lang === "en" ? EN["dl.meta"] : meta.dataset.zh} · ${mb}`;
    const cmd = document.querySelector(".js-hash-cmd");
    if (cmd) cmd.textContent = `Get-FileHash .\\${release.name} -Algorithm SHA256`;
  }

  // ---------- Screenshots: images/<shot>.<lang>.png, shown one at a time on the stage ----------
  // The section stays hidden until at least one screenshot loads; tabs whose image is missing are hidden.
  const shotUrls = {};
  let currentShot = null;

  function showShot(name, focus) {
    const tab = document.getElementById(`tab-${name}`);
    const img = document.querySelector(".sc-img");
    if (!tab || !img || !shotUrls[name]) return;
    currentShot = name;
    $$(".sc-tab").forEach((t) => {
      const on = t === tab;
      t.setAttribute("aria-selected", on ? "true" : "false");
      t.tabIndex = on ? 0 : -1;
    });
    const stage = document.getElementById("sc-stage");
    stage.setAttribute("aria-labelledby", tab.id);
    stage.dataset.shot = name;
    img.classList.remove("in");
    img.src = shotUrls[name];
    img.alt = tab.querySelector(".sc-t").textContent;
    // Restart the fade-in on every switch.
    void img.offsetWidth;
    img.classList.add("in");
    if (focus) tab.focus();
  }

  function loadScreenshots() {
    const section = document.getElementById("screens");
    if (!section) return;
    const other = lang === "en" ? "zh-Hant" : "en";
    const wanted = currentShot || $$(".sc-tab")[0]?.dataset.shot;
    let wantedMissing = false;
    currentShot = null;
    $$(".sc-tab").forEach((tab) => {
      const name = tab.dataset.shot;
      tab.hidden = true;
      delete shotUrls[name];
      const probe = new Image();
      probe.onload = () => {
        shotUrls[name] = probe.src;
        tab.hidden = false;
        section.hidden = false;
        // Keep the current tab (refreshed in the new language), or start on the first one.
        if (name === wanted) showShot(name);
        else if (wantedMissing && !currentShot) showShot(name);
      };
      // Fall back to the other language's screenshot before giving up.
      probe.onerror = () => {
        if (!probe.dataset.fallback) { probe.dataset.fallback = "1"; probe.src = `images/${name}.${other}.png`; return; }
        if (name !== wanted) return;
        wantedMissing = true;
        const loaded = $$(".sc-tab").find((t) => shotUrls[t.dataset.shot]);
        if (loaded) showShot(loaded.dataset.shot);
      };
      probe.src = `images/${name}.${lang}.png`;
    });
  }

  $$(".sc-tab").forEach((tab) => tab.addEventListener("click", () => showShot(tab.dataset.shot)));
  document.querySelector(".sc-tabs")?.addEventListener("keydown", (e) => {
    const keys = { ArrowDown: 1, ArrowRight: 1, ArrowUp: -1, ArrowLeft: -1 };
    if (!(e.key in keys)) return;
    e.preventDefault();
    const tabs = $$(".sc-tab").filter((t) => !t.hidden);
    const i = tabs.findIndex((t) => t.dataset.shot === currentShot);
    const next = tabs[(i + keys[e.key] + tabs.length) % tabs.length];
    if (next) showShot(next.dataset.shot, true);
  });

  // ---------- Ko-fi button in the download section, drawn by Ko-fi's Widget_2.js ----------
  function drawKofiWidget() {
    const box = document.querySelector(".kofi-widget");
    if (!box || typeof kofiwidget2 === "undefined") return;
    kofiwidget2.init(lang === "en" ? EN["support.btn"] : "在 Ko-fi 支持我", "#72a4f2", "R0V52861LJ");
    // getHTML instead of draw(): draw() uses document.writeln, which only works while the page is loading.
    box.innerHTML = kofiwidget2.getHTML();
  }

  // ---------- Ko-fi: open the donation panel in a dialog; the panel itself only loads after a click ----------
  const KOFI_EMBED = "https://ko-fi.com/yueyang666/?hidefeed=true&widget=true&embed=true";
  const kofiDialog = document.getElementById("kofi-dialog");
  if (kofiDialog && typeof kofiDialog.showModal === "function") {
    const frame = kofiDialog.querySelector(".kofi-frame");
    // Delegated, because the Ko-fi widget button is redrawn on every language switch.
    document.addEventListener("click", (e) => {
      const a = e.target.closest(".js-kofi, .kofi-button");
      if (!a) return;
      // Let modified clicks (new tab/window) behave like a normal link.
      if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      // Ko-fi's panel needs about 440px; on phones open the Ko-fi page itself.
      if (window.innerWidth < 480) return;
      e.preventDefault();
      if (!frame.src) frame.src = KOFI_EMBED;
      kofiDialog.showModal();
    });
    kofiDialog.querySelector("[data-close]").addEventListener("click", () => kofiDialog.close());
    // Clicking the backdrop closes the dialog.
    kofiDialog.addEventListener("click", (e) => { if (e.target === kofiDialog) kofiDialog.close(); });
  }

  applyLanguage();
  fetchRelease();
})();
