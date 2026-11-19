// Draws the three monthly trend charts on the Player Profile page.
// Each metric gets its own small chart (one series, one axis) instead of
// sharing a dual-axis chart. The table under the charts has the same numbers.
(function () {
    const source = document.getElementById("trend-data");
    if (!source || typeof Chart === "undefined") return;

    const data = JSON.parse(source.textContent);
    const css = getComputedStyle(document.documentElement);
    const token = (name) => css.getPropertyValue(name).trim();
    const accent = token("--accent");
    const muted = token("--muted");
    const grid = token("--line");
    const surface = token("--surface");
    const lastIndex = data.labels.length - 1;

    Chart.defaults.font.family = token("--body") || "system-ui";
    Chart.defaults.color = muted;

    function lineChart(canvasId, values, { format, min, max }) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        new Chart(canvas, {
            type: "line",
            data: {
                labels: data.labels,
                datasets: [{
                    data: values,
                    borderColor: accent,
                    backgroundColor: accent,
                    borderWidth: 2,
                    borderJoinStyle: "round",
                    borderCapStyle: "round",
                    tension: 0.25,
                    spanGaps: true,
                    // Only the latest month gets a dot; hovering shows the rest.
                    pointRadius: (ctx) => (ctx.dataIndex === lastIndex ? 4 : 0),
                    pointHoverRadius: 5,
                    pointBorderColor: surface,
                    pointBorderWidth: 2
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? false : { duration: 300 },
                interaction: { mode: "index", intersect: false },
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        displayColors: false,
                        callbacks: {
                            label: (ctx) => `${format(ctx.parsed.y)} · ${data.games[ctx.dataIndex]} matches`
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        border: { color: grid },
                        ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 6 }
                    },
                    y: {
                        min, max,
                        grid: { color: grid, lineWidth: 1 },
                        border: { display: false },
                        ticks: { maxTicksLimit: 5, callback: (v) => format(v) }
                    }
                }
            }
        });
    }

    lineChart("chart-kd", data.kd, { format: (v) => Number(v).toFixed(2) });
    lineChart("chart-adr", data.adr, { format: (v) => Number(v).toFixed(0) });
    lineChart("chart-win", data.winRate, { format: (v) => `${Number(v).toFixed(0)}%`, min: 0, max: 100 });
})();
