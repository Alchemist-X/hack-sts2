import copy
import unittest

from analyze_current import ARMS, BATCH, M_PROFILE_OVERLAY, terminal_dose
from eval_summary import wilson
from paired_statistics import ENDPOINTS, paired_deltas, validate_statistics_parameters

class Tests(unittest.TestCase):
    def evidence(self, name, updates):
        arm = ARMS[name]
        full = updates == arm["target_update"]
        decision = {"sha256": "d"*64}
        model = dict(updates=updates, decisions=updates*BATCH, checkpoint_file_sha256="a"*64, bytes=100, input_arch="B0", critic_arch="candidate")
        terminal = dict(classification=arm["full_label"] if full else "resource_censored_terminal", final_stop=True, further_training_authorized=False, midpoint_evaluation=False, nominal_primary_observed=full)
        ready = dict(science_plan_sha256=arm["science"], root_terminal_decision_sha256=decision["sha256"], terminal_evaluation=terminal)
        if name == "M116":
            ready.update(model_profile="M-current-recipe", base_params=16253584, transition_params=74052, profile_overlay=M_PROFILE_OVERLAY)
        pre = dict(ready, arm=arm["arm"], strategic_forward_kl={"beta":0}, input_arch=model["input_arch"], critic_arch=model["critic_arch"])
        return model, ready, pre, decision

    def test_current_segment_dose_and_arm_rejects_substitute(self):
        s = terminal_dose("S115_S2", *self.evidence("S115_S2", 5309))
        m = terminal_dose("M116", *self.evidence("M116", 763))
        self.assertEqual((s["cumulative_decisions"], m["cumulative_decisions"]), (695861248, 100007936))
        self.assertEqual(s["current_call_new_decisions"], m["current_call_new_decisions"])
        self.assertEqual(terminal_dose("S115_S2", *self.evidence("S115_S2", 5000))["terminal_evaluation"]["classification"], "resource_censored_terminal")
        with self.assertRaises(ValueError):
            terminal_dose("S115_S2", *self.evidence("S115_S2", 4546))
        with self.assertRaises(ValueError):
            terminal_dose("M116", *self.evidence("S115_S2", 763))
        b = list(self.evidence("M116", 763))
        b[2] = dict(b[2], base_params=2941072)
        with self.assertRaises(ValueError):
            terminal_dose("M116", *b)

    def test_pair_identity_act_sign_and_zero_win_wilson(self):
        keys = [f"key{i:03d}" for i in range(256)]
        s = [dict(key=k, progress=.5, result="loss", act=1) for k in keys]
        m = [dict(k, progress=.75, act=2) for k in s]
        p = paired_deltas(m, s, keys)[0]
        self.assertEqual((p["progress_delta"], p["win_indicator_delta"], p["act2_reach_delta"], p["act3_reach_delta"]), (.25,0,1,0))
        with self.assertRaises(ValueError):
            paired_deltas(m[:-1],s,keys)
        m[-1]["key"] = keys[0]
        with self.assertRaises(ValueError):
            paired_deltas(m,s,keys)
        params = validate_statistics_parameters()
        self.assertEqual((params["seed"],params["replicates"],params["n"],params["endpoints"]), (121116115,20000,256,list(ENDPOINTS)))
        self.assertGreater(wilson(0)[1],0)
