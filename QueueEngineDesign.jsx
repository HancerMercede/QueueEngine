import { useState } from "react";

const C = {
  bg: "#06080C",
  surface: "#0C0F16",
  surface2: "#111520",
  border: "#1A2035",
  cyan: "#00E5FF",
  amber: "#F59E0B",
  green: "#22C55E",
  rose: "#F43F5E",
  purple: "#A855F7",
  blue: "#3B82F6",
  muted: "#3D4F6B",
  textDim: "#64748B",
  text: "#CBD5E1",
  textBright: "#F1F5F9",
};

function Box({ children, color = "#00E5FF", style = {}, onClick, glow }) {
  const [hov, setHov] = useState(false);
  return (
    <div
      onClick={onClick}
      onMouseEnter={() => setHov(true)}
      onMouseLeave={() => setHov(false)}
      style={{
        background: C.surface,
        border: `1px solid ${hov ? color : C.border}`,
        borderRadius: 10,
        position: "relative",
        overflow: "hidden",
        cursor: onClick ? "pointer" : "default",
        transition: "all 0.22s ease",
        transform: hov ? "translateY(-2px)" : "none",
        boxShadow: hov || glow ? `0 0 28px ${color}28, inset 0 0 40px ${color}05` : "none",
        ...style,
      }}
    >
      <div style={{
        position: "absolute", top: 0, left: 0, right: 0, height: 2,
        background: `linear-gradient(90deg, transparent, ${color}, transparent)`,
        opacity: hov ? 1 : 0.4, transition: "opacity 0.22s",
      }} />
      {children}
    </div>
  );
}

function Pill({ children, color = "#00E5FF", dot = true }) {
  return (
    <span style={{
      display: "inline-flex", alignItems: "center", gap: 5,
      fontSize: 10, padding: "3px 9px", borderRadius: 20,
      border: `1px solid ${color}44`, background: `${color}10`,
      color, fontFamily: "monospace", whiteSpace: "nowrap",
    }}>
      {dot && <span style={{ width: 6, height: 6, borderRadius: "50%", background: color, flexShrink: 0 }} />}
      {children}
    </span>
  );
}

function Label({ children }) {
  return (
    <div style={{
      fontFamily: "monospace", fontSize: 10,
      letterSpacing: "0.3em", textTransform: "uppercase",
      color: C.muted, display: "flex", alignItems: "center", gap: 12,
      marginBottom: 18,
    }}>
      {children}
      <div style={{ flex: 1, height: 1, background: C.border }} />
    </div>
  );
}

function Connector({ label, color = C.muted }) {
  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: "center", padding: "2px 0", gap: 2 }}>
      {label && (
        <span style={{ fontSize: 9, color: C.muted, fontFamily: "monospace", letterSpacing: 1 }}>{label}</span>
      )}
      <svg width={2} height={36} style={{ overflow: "visible" }}>
        <line x1={1} y1={0} x2={1} y2={36} stroke={color} strokeWidth={1.5} strokeDasharray="5 4" />
        <polygon points="1,36 -2,30 4,30" fill={color} />
      </svg>
    </div>
  );
}

function EntryLayer() {
  return (
    <div>
      <Label>Entry Layer</Label>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 10 }}>
        {[
          { icon: "🌐", title: "ASP.NET Core API", sub: "EnqueueAsync()\nBulkEnqueueAsync()", color: C.blue, tags: ["HTTP", "REST"] },
          { icon: "⏰", title: "Scheduled Jobs", sub: "scheduledAt: DateTime\nDeferred execution", color: C.purple, tags: ["Cron", "Delayed"] },
          { icon: "📦", title: "Console / Worker", sub: "Standalone mode\nNo DI required", color: C.cyan, tags: ["CLI", "Service"] },
        ].map(({ icon, title, sub, color, tags }) => (
          <Box key={title} color={color} style={{ padding: "16px 14px" }}>
            <div style={{ fontSize: 22, marginBottom: 8 }}>{icon}</div>
            <div style={{ fontFamily: "sans-serif", fontSize: 12, fontWeight: 700, color: C.textBright, marginBottom: 4 }}>{title}</div>
            <div style={{ fontFamily: "monospace", fontSize: 11, color: C.textDim, lineHeight: 1.7, whiteSpace: "pre-line" }}>{sub}</div>
            <div style={{ marginTop: 8, display: "flex", gap: 4, flexWrap: "wrap" }}>
              {tags.map(t => <Pill key={t} color={color} dot={false}>{t}</Pill>)}
            </div>
          </Box>
        ))}
      </div>
    </div>
  );
}

function EngineCore() {
  const [sel, setSel] = useState(null);
  const components = [
    {
      key: "core", icon: "⚙️", color: C.cyan,
      title: "QueueEngine (Core)",
      sub: "Main facade. Orchestrates workers, repository and handlers.",
      api: ["EnqueueAsync()", "BulkEnqueueAsync()", "CancelJobAsync()", "GetJobAsync()", "GetStatsAsync()", "StartAsync() / StopAsync()"],
    },
    {
      key: "worker", icon: "👷", color: C.amber,
      title: "QueueWorkerPool",
      sub: "Worker pool with SemaphoreSlim. Dispatch loop + stall detection.",
      api: ["Concurrency per queue", "SELECT FOR UPDATE SKIP LOCKED", "Stall check every 2 min", "Pause / Resume"],
    },
    {
      key: "rate", icon: "🚦", color: C.green,
      title: "RateLimiter",
      sub: "Thread-safe token bucket. Per second and/or per minute.",
      api: ["RateLimitPerSecond", "RateLimitPerMinute", "WaitAsync()"],
    },
    {
      key: "repo", icon: "🗄️", color: C.purple,
      title: "JobRepository",
      sub: "Dapper ORM. Compatible with SQLite (dev) and PostgreSQL (prod).",
      api: ["Atomic DequeueAsync()", "MarkCompleted/Failed()", "RequeueStalledJobs()", "GetDeadLetterJobs()"],
    },
  ];

  return (
    <div>
      <Label>Core Engine</Label>
      <Box color={C.cyan} glow style={{ padding: "20px 20px 16px" }}>
        <div style={{
          display: "flex", alignItems: "center", gap: 10, marginBottom: 18,
          fontFamily: "sans-serif", fontSize: 15, fontWeight: 800, color: C.cyan,
        }}>
          <span style={{
            width: 10, height: 10, borderRadius: "50%", background: C.cyan, flexShrink: 0,
            animation: "pulse 2s infinite",
          }} />
          QUEUE ENGINE — .NET 10
          <span style={{ marginLeft: "auto" }}><Pill color={C.green}>RUNNING</Pill></span>
        </div>

        <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: 10 }}>
          {components.map(c => (
            <div
              key={c.key}
              onClick={() => setSel(sel === c.key ? null : c.key)}
              style={{
                background: sel === c.key ? `${c.color}10` : C.surface2,
                border: `1px solid ${sel === c.key ? c.color : C.border}`,
                borderLeft: `3px solid ${c.color}`,
                borderRadius: 8, padding: "14px",
                cursor: "pointer", transition: "all 0.2s",
              }}
            >
              <div style={{ display: "flex", alignItems: "center", gap: 8, marginBottom: 6 }}>
                <span style={{ fontSize: 18 }}>{c.icon}</span>
                <span style={{ fontFamily: "monospace", fontSize: 12, fontWeight: 700, color: c.color }}>{c.title}</span>
              </div>
              <div style={{ fontFamily: "monospace", fontSize: 10, color: C.textDim, lineHeight: 1.6 }}>{c.sub}</div>
              {sel === c.key && (
                <div style={{ marginTop: 10, display: "flex", flexDirection: "column", gap: 4 }}>
                  {c.api.map(a => (
                    <div key={a} style={{
                      fontSize: 10, color: c.color, fontFamily: "monospace",
                      background: `${c.color}08`, borderRadius: 4, padding: "3px 8px",
                    }}>
                      › {a}
                    </div>
                  ))}
                </div>
              )}
            </div>
          ))}
        </div>
      </Box>
    </div>
  );
}

function QueuesLayer() {
  const queues = [
    { name: "default",     concurrency: 4, rate: "20/s",   priority: "Normal",   color: C.cyan,   jobs: 12, running: 3 },
    { name: "critical",    concurrency: 2, rate: "∞",      priority: "Critical", color: C.rose,   jobs: 2,  running: 2 },
    { name: "background",  concurrency: 1, rate: "30/min", priority: "Low",      color: C.muted,  jobs: 8,  running: 1 },
    { name: "dead-letter", concurrency: 0, rate: "—",      priority: "—",        color: C.amber,  jobs: 3,  running: 0 },  ];
  return (
    <div>
      <Label>Configured Queues</Label>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 10 }}>
        {queues.map(q => (
          <Box key={q.name} color={q.color} style={{ padding: "14px" }}>
            <div style={{ fontFamily: "monospace", fontSize: 11, fontWeight: 700, color: q.color, marginBottom: 10 }}>
              [{q.name}]
            </div>
            <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
              {[["concurrency", q.concurrency], ["rate limit", q.rate], ["priority", q.priority]].map(([k, v]) => (                <div key={k} style={{ display: "flex", justifyContent: "space-between" }}>
                  <span style={{ fontSize: 10, color: C.muted, fontFamily: "monospace" }}>{k}</span>
                  <span style={{ fontSize: 10, color: C.text, fontFamily: "monospace" }}>{v}</span>
                </div>
              ))}
            </div>
            <div style={{ marginTop: 12, paddingTop: 10, borderTop: `1px solid ${C.border}`, display: "flex", gap: 12 }}>
              <div>
                <div style={{ fontSize: 18, fontWeight: 800, color: q.color, fontFamily: "sans-serif" }}>{q.jobs}</div>
                <div style={{ fontSize: 9, color: C.muted, fontFamily: "monospace" }}>pending</div>
              </div>
              <div>
                <div style={{ fontSize: 18, fontWeight: 800, color: C.green, fontFamily: "sans-serif" }}>{q.running}</div>
                <div style={{ fontSize: 9, color: C.muted, fontFamily: "monospace" }}>running</div>              </div>
            </div>
          </Box>
        ))}
      </div>
    </div>
  );
}

function HandlersLayer() {
  const handlers = [
    { name: "EmailJobHandler",     type: "send-email",        color: C.blue,   icon: "✉️" },
    { name: "NotificationHandler", type: "send-notification", color: C.purple, icon: "🔔" },
    { name: "ReportJobHandler",    type: "generate-report",   color: C.amber,  icon: "📊" },
    { name: "CustomJobHandler",    type: "your-job-type",     color: C.muted,  icon: "🔧" },
  ];
  return (
    <div>
      <Label>Registered Handlers</Label>
      <div style={{ display: "grid", gridTemplateColumns: "repeat(4,1fr)", gap: 10 }}>
        {handlers.map(h => (
          <Box key={h.name} color={h.color} style={{ padding: "14px" }}>
            <div style={{ fontSize: 20, marginBottom: 8 }}>{h.icon}</div>
            <div style={{ fontFamily: "monospace", fontSize: 11, fontWeight: 600, color: h.color, marginBottom: 4 }}>{h.name}</div>
            <div style={{ fontFamily: "monospace", fontSize: 10, color: C.muted }}>JobType: "{h.type}"</div>
            <div style={{ marginTop: 8 }}><Pill color={h.color} dot={false}>JobHandler&lt;T&gt;</Pill></div>
          </Box>
        ))}
      </div>
    </div>
  );
}

function DatabaseLayer() {
  return (
    <div>
      <Label>Persistence Layer</Label>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr", gap: 10 }}>
        {[
          { icon: "🐘", title: "PostgreSQL", sub: "Production. SELECT FOR UPDATE\nSKIP LOCKED for atomic dequeue.", color: C.blue, tags: ["prod", "concurrent"] },
          { icon: "🗃️", title: "SQLite", sub: "Development. No additional\nsetup required. Local file.", color: C.cyan, tags: ["dev", "local"] },
          { icon: "📋", title: "queue_jobs", sub: "id · queue_name · job_type\npayload · status · priority\nattempt_count · next_retry_at", color: C.purple, tags: ["Dapper ORM"] },
        ].map(({ icon, title, sub, color, tags }) => (
          <Box key={title} color={color} style={{ padding: "16px" }}>
            <div style={{ fontSize: 22, marginBottom: 8 }}>{icon}</div>
            <div style={{ fontFamily: "sans-serif", fontSize: 13, fontWeight: 700, color, marginBottom: 6 }}>{title}</div>
            <div style={{ fontFamily: "monospace", fontSize: 10, color: C.textDim, lineHeight: 1.7, whiteSpace: "pre-line" }}>{sub}</div>
            <div style={{ marginTop: 10, display: "flex", gap: 4, flexWrap: "wrap" }}>
              {tags.map(t => <Pill key={t} color={color} dot={false}>{t}</Pill>)}
            </div>
          </Box>
        ))}
      </div>
    </div>
  );
}

function StatusFlow() {
  const [active, setActive] = useState(null);
  const main  = [
    { key: "PENDING",    color: C.amber,  icon: "⏳", desc: "Waiting for a free worker" },
    { key: "RUNNING",    color: C.cyan,   icon: "⚡", desc: "Worker executing handler" },
    { key: "COMPLETED",  color: C.green,  icon: "✓",  desc: "Success — persisted" },
  ];
  const alt = [
    { key: "RETRYING",    color: C.purple, icon: "↺",  desc: "Exponential backoff" },
    { key: "CANCELLED",   color: C.textDim,icon: "✕",  desc: "CancelJobAsync()" },
    { key: "DEAD LETTER", color: C.rose,   icon: "☠",  desc: "Max attempts reached" },
  ];
  return (
    <div>
      <Label>Job Lifecycle</Label>
      <div style={{ display: "flex", alignItems: "center", flexWrap: "wrap", gap: 6, marginBottom: 12 }}>
        {main.map((s, i) => (
          <div key={s.key} style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <div
              onClick={() => setActive(active === s.key ? null : s.key)}
              style={{
                display: "flex", alignItems: "center", gap: 8,
                background: active === s.key ? `${s.color}18` : C.surface,
                border: `1px solid ${active === s.key ? s.color : C.border}`,
                borderRadius: 24, padding: "8px 16px", cursor: "pointer",
                transition: "all 0.2s",
              }}
            >
              <span style={{ fontSize: 14 }}>{s.icon}</span>
              <div>
                <div style={{ fontFamily: "monospace", fontSize: 11, fontWeight: 600, color: s.color }}>{s.key}</div>
                {active === s.key && <div style={{ fontFamily: "monospace", fontSize: 10, color: C.textDim }}>{s.desc}</div>}
              </div>
            </div>
            {i < main.length - 1 && (
              <svg width={28} height={14} style={{ flexShrink: 0 }}>
                <line x1={0} y1={7} x2={24} y2={7} stroke={C.muted} strokeWidth={1.5} strokeDasharray="4 3" />
                <polygon points="28,7 22,4 22,10" fill={C.muted} />
              </svg>
            )}
          </div>
        ))}
      </div>
      <div style={{
        background: C.surface2, border: `1px solid ${C.border}`, borderRadius: 8,
        padding: "14px 16px", display: "flex", alignItems: "center", gap: 8, flexWrap: "wrap",
      }}>
        <span style={{ fontSize: 10, color: C.muted, fontFamily: "monospace", marginRight: 4 }}>ON FAILURE →</span>
        {alt.map((s, i) => (
          <div key={s.key} style={{ display: "flex", alignItems: "center", gap: 6 }}>
            <Pill color={s.color}>{s.icon} {s.key}</Pill>
            {i < alt.length - 1 && <span style={{ color: C.border }}>·</span>}
          </div>
        ))}
        <span style={{ marginLeft: "auto", fontFamily: "monospace", fontSize: 10, color: C.textDim }}>
          RetryDeadLetterAsync() ↺
        </span>
      </div>
    </div>
  );
}

function ObservabilityLayer() {
  return (
    <div>
      <Label>Observability & Ops</Label>
      <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr 1fr 1fr", gap: 10 }}>
        {[
          { icon: "❤️", title: "HealthCheck",     sub: "ASP.NET Core\n/health endpoint",             color: C.green  },
          { icon: "📈", title: "GetStatsAsync()",  sub: "pending · running\ncompleted · failed",       color: C.cyan   },
          { icon: "🪵", title: "Logging",          sub: "Structured ILogger\nMicrosoft.Extensions",   color: C.amber  },
          { icon: "🧪", title: "Tests",            sub: "QueueEngine.Tests\nMockable interfaces",      color: C.purple },
        ].map(({ icon, title, sub, color }) => (
          <Box key={title} color={color} style={{ padding: "14px" }}>
            <div style={{ fontSize: 20, marginBottom: 8 }}>{icon}</div>
            <div style={{ fontFamily: "sans-serif", fontSize: 12, fontWeight: 700, color, marginBottom: 4 }}>{title}</div>
            <div style={{ fontFamily: "monospace", fontSize: 10, color: C.textDim, lineHeight: 1.7, whiteSpace: "pre-line" }}>{sub}</div>
          </Box>
        ))}
      </div>
    </div>
  );
}

export default function QueueEngineDiagram() {
  return (
    <div style={{
      background: C.bg, minHeight: "100vh", color: C.text,
      padding: "40px 32px", position: "relative", overflow: "hidden",
    }}>
      <style>{`
        @keyframes pulse { 0%,100%{opacity:1;box-shadow:0 0 0 0 rgba(0,229,255,0.5)} 50%{opacity:0.7;box-shadow:0 0 0 6px rgba(0,229,255,0)} }
      `}</style>

      {/* Grid bg */}
      <div style={{
        position: "fixed", inset: 0, pointerEvents: "none",
        backgroundImage: `linear-gradient(${C.border}66 1px, transparent 1px), linear-gradient(90deg, ${C.border}66 1px, transparent 1px)`,
        backgroundSize: "40px 40px",
      }} />

      <div style={{ maxWidth: 1040, margin: "0 auto", position: "relative", zIndex: 1 }}>

        {/* Header */}
        <div style={{ textAlign: "center", marginBottom: 52 }}>
          <div style={{
            display: "inline-block", fontSize: 10, letterSpacing: "0.3em",
            textTransform: "uppercase", color: C.cyan,
            border: `1px solid ${C.cyan}44`, padding: "4px 14px",
            borderRadius: 2, marginBottom: 14, fontFamily: "monospace",
          }}>
            System Design
          </div>
          <div style={{
            fontFamily: "sans-serif", fontSize: 38, fontWeight: 800,
            letterSpacing: -1, lineHeight: 1,
            background: `linear-gradient(135deg, #fff 30%, ${C.cyan})`,
            WebkitBackgroundClip: "text", WebkitTextFillColor: "transparent",
            backgroundClip: "text", marginBottom: 10,
          }}>
            QueueEngine
          </div>
          <div style={{ fontFamily: "monospace", fontSize: 12, color: C.muted }}>
            .NET 10 · SQLite / PostgreSQL · ASP.NET Core · branch: develop
          </div>
        </div>

        {/* Layer stack */}
        <div style={{ display: "flex", flexDirection: "column", gap: 10 }}>
          <EntryLayer />
          <Connector label="IQueueEngine.EnqueueAsync()" color={C.cyan} />
          <EngineCore />
          <Connector label="DequeueAsync() → IJobHandler.ExecuteAsync()" color={C.amber} />
          <QueuesLayer />
          <Connector label="JobType dispatch" color={C.purple} />
          <HandlersLayer />
          <Connector label="JobRepository (Dapper)" color={C.blue} />
          <DatabaseLayer />
          <div style={{ height: 1, background: C.border, margin: "6px 0" }} />
          <StatusFlow />
          <div style={{ height: 1, background: C.border, margin: "6px 0" }} />
          <ObservabilityLayer />
        </div>

        {/* Footer */}
        <div style={{
          marginTop: 40, textAlign: "center", fontFamily: "monospace",
          fontSize: 11, color: C.muted,
          display: "flex", alignItems: "center", justifyContent: "center", gap: 16,
        }}>
          <span>github.com/HancerMercede/QueueEngine</span>
          <span style={{ color: C.border }}>·</span>
          <Pill color={C.green}>active</Pill>
        </div>
      </div>
    </div>
  );
}
