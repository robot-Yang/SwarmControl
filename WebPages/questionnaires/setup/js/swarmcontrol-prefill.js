// SwarmControl integration: read ?pid=...&task=...&camipro=... from the URL
// and prefill the Step 1 forms (or select existing entries if they match).
// Runs after default.js so its document-ready handlers (incl. localStorage
// restore) have already populated the participant/task lists.
(function () {
	function parseQuery() {
		var params = {};
		var query = window.location.search.replace(/^\?/, "");
		if (!query) { return params; }
		var pairs = query.split("&");
		for (var i = 0; i < pairs.length; i++) {
			var kv = pairs[i].split("=");
			if (!kv[0]) { continue; }
			params[decodeURIComponent(kv[0])] = decodeURIComponent((kv[1] || "").replace(/\+/g, " "));
		}
		return params;
	}

	function selectByLabel($container, needle) {
		var matched = false;
		$container.find("label").each(function () {
			var text = $(this).text().toLowerCase();
			if (text === needle || text.indexOf(needle) !== -1) {
				var id = $(this).attr("for");
				$container.find("input[type='radio'][id='" + id + "']").prop("checked", true);
				matched = true;
				return false;
			}
		});
		return matched;
	}

	$(function () {
		var q = parseQuery();
		if (!q.pid && !q.task) { return; }

		var advance = true;

		if (q.pid) {
			var needle = q.pid.toLowerCase();
			var picked = selectByLabel($(".step_1 .first .list"), needle);
			if (!picked) {
				$("#create_proband_name").val(q.pid);
				if (q.camipro) { $("#create_proband_camipro").val(q.camipro); }
				advance = false; // experimenter still needs to click Create
			}
		}

		if (q.task) {
			var needleTask = q.task.toLowerCase();
			var pickedTask = selectByLabel($(".step_1 .second .list"), needleTask);
			if (!pickedTask) {
				$("#create_task").val(q.task);
				advance = false;
			}
		}

		// Jump from the overview to Step 1 so the experimenter lands on the
		// right screen. If everything was already selectable, they only need
		// to choose a questionnaire and click Continue.
		if ($("#go_step_1").is(":visible")) {
			$("#go_step_1").click();
		}

		// Surface what we did so it's obvious in the UI.
		var note = "SwarmControl: prefilled from URL (pid=" + (q.pid || "-") + ", task=" + (q.task || "-") + ").";
		$(".step_1").prepend("<p class='swarmcontrol-note' style='background:#ffe;border:1px solid #cc9;padding:6px 10px;margin:0 0 10px;'>" + note + "</p>");

		// advance flag is informational; we always show Step 1 so the
		// experimenter can verify before continuing.
		void advance;
	});
})();
