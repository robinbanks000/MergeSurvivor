# Lessons

Rules earned the hard way. A lesson that can become a test should stop being a
lesson — see `lessons-promoter`.

## L-001 — A specialist testing edge cases will reason forward from the code

Observed in the first canary. The verification engineer tested `Tick(float.NaN)`,
wrote the comment "NaN comparison < 0 is always false, so NaN passes the dt < 0
check", and asserted the resulting behaviour was correct. The criterion said a
*non-throwing* Tick must increment; it never said NaN must be non-throwing. The
specialist inferred the premise from the guard and then verified the guard
against it, producing a passing test that certifies a defect as intended.

The rule: when a test's comment explains *why the implementation does what it
does*, the test is confirming the code, not the specification. Reviewers should
treat that phrasing as a signal.

Promotion candidate: an analyzer that flags assertions whose surrounding comment
names an implementation detail of the code under test.

## L-002 — Fixing a defect can require inverting a passing test, and that must be authorised

PRO-0002's fix inverts three currently-passing assertions. An implementer who
rewrites them without explicit authorisation in the work order is
indistinguishable from one weakening tests to go green. Any work order whose fix
inverts an existing assertion must say so and name the assertions.
