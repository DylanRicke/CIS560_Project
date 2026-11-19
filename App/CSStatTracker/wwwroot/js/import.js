// Import Match page helpers. The page works without this script; it only
// trims the player rows to the chosen mode's team size and previews the result.
(function () {
    const form = document.getElementById("import-form");
    if (!form) return;

    const mode = document.getElementById("mode");
    const score1 = document.getElementById("score1");
    const score2 = document.getElementById("score2");

    function selectedMode() {
        const option = mode.options[mode.selectedIndex];
        return {
            perTeam: parseInt(option?.dataset.playersPerTeam || "10", 10),
            maxRounds: parseInt(option?.dataset.maxRounds || "60", 10)
        };
    }

    // Show only as many rows as the mode allows. Hidden rows are disabled so
    // the browser doesn't submit them.
    function applyTeamSize() {
        const { perTeam, maxRounds } = selectedMode();
        form.querySelectorAll(".team-block tbody tr").forEach((row) => {
            const index = parseInt(row.dataset.row, 10);
            const visible = index < perTeam;
            row.hidden = !visible;
            row.querySelectorAll("input, select").forEach((el) => (el.disabled = !visible));
        });
        score1.max = maxRounds;
        score2.max = maxRounds;
    }

    function previewOutcome() {
        const a = parseInt(score1.value, 10);
        const b = parseInt(score2.value, 10);
        const label = (mine, theirs) =>
            Number.isNaN(mine) || Number.isNaN(theirs) ? "" : mine > theirs ? "Wins" : mine < theirs ? "Loses" : "Tie";
        form.querySelector('[data-outcome="1"]').textContent = label(a, b);
        form.querySelector('[data-outcome="2"]').textContent = label(b, a);
    }

    // Grey out a player in other rows once they're picked, so nobody is entered twice.
    function markTakenPlayers() {
        const selects = [...form.querySelectorAll('.team-block select')];
        const taken = new Set(selects.filter((s) => !s.disabled && s.value).map((s) => s.value));
        selects.forEach((s) => {
            [...s.options].forEach((o) => {
                if (!o.value) return;
                o.disabled = taken.has(o.value) && s.value !== o.value;
            });
        });
    }

    mode.addEventListener("change", () => { applyTeamSize(); markTakenPlayers(); });
    score1.addEventListener("input", previewOutcome);
    score2.addEventListener("input", previewOutcome);
    form.addEventListener("change", (e) => {
        if (e.target.matches(".team-block select")) markTakenPlayers();
    });

    applyTeamSize();
    previewOutcome();
    markTakenPlayers();
})();
