using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		/// <summary>
		/// 視点の向きを外部から設定する（部屋の出入りなどのテレポート時に使う）。
		/// yaw=水平の向き、pitch=上下の向き。内部の累積値も合わせて更新するので
		/// 次のフレームで元の向きに戻されることがない。
		/// </summary>
		public void SetLook(float yaw, float pitch = 0f)
		{
			transform.rotation = Quaternion.Euler(0f, yaw, 0f);
			_cinemachineTargetPitch = ClampAngle(pitch, BottomClamp, TopClamp);
			if (CinemachineCameraTarget != null)
				CinemachineCameraTarget.transform.localRotation =
					Quaternion.Euler(_cinemachineTargetPitch, 0f, 0f);
		}

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		// 角の補正：家具の角や細い柱にほぼ正面から当たって止まる時、横へ少しずれれば抜けられるなら滑らせて抜けさせる
		// （壁のように横へずれても抜けられない物には何もしない）
		private const float CornerAssist = 0.33f;                       // 横へ逃がす最大量（m）。体の半径ほど＝角の真正面に当たっても抜けられる
		private static readonly float CornerCos = Mathf.Cos(40f * Mathf.Deg2Rad);   // これより正面寄りに当たった時だけ
		private readonly RaycastHit[] _castHits = new RaycastHit[8];
		private int _collideMask = -1;

		// 押せる家具（椅子など、EscapeProto.PushableProp）：体が動く前に、その一歩で当たる分だけ先にどかす（ShovePushables）
		private const float ShoveMargin = 0.01f;   // どかす時に体との間に空ける余り（m）
		private static readonly float ShoveHeadOnCos = Mathf.Cos(20f * Mathf.Deg2Rad);   // これより正面寄りなら歩く向きへどかす
		private static readonly float ShoveGlanceCos = Mathf.Cos(50f * Mathf.Deg2Rad);   // これより斜めなら当たる面の向きへ
		// そのほかの動く Rigidbody（壊れた破片など）は当たると押して滑らせる
		private const float PushSpeed = 1.4f;

		// マウス由来の視点はdeltaTimeを掛けない（StarterAssetsInputsが判定）
		private bool IsCurrentDeviceMouse => _input == null || _input.lookIsMouse;

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			// 高いフレームレート（約270fps以上）だと歩き出しの1フレームの移動量が最小移動距離(既定0.001m)を
			// 下回って捨てられ、速度0のまま加速が始まらない（Wを押しても動き出すまで遅れる）。0にして必ず動かす
			_controller.minMoveDistance = 0f;
			_input = GetComponent<StarterAssetsInputs>();

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
		}

		private void Update()
		{
			JumpAndGravity();
			GroundedCheck();
			Move();
		}

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
				
				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// Update Cinemachine camera target pitch
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
		}

		private void Move()
		{
			// set target speed based on move speed, sprint speed and if sprint is pressed
			float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

			// a simplistic acceleration and deceleration designed to be easy to remove, replace, or iterate upon

			// note: Vector2's == operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is no input, set the target speed to 0
			if (_input.move == Vector2.zero) targetSpeed = 0.0f;

			// 今の速さは「出そうとしている速さ」で数える（実際に動けた速さで数えると、椅子や壁に当たって遅れた後に
			// 歩く速さへ急に戻る＝外れた時に加速して見える。障害物は歩く速さを削らない）
			float currentHorizontalSpeed = _speed;

			float speedOffset = 0.1f;
			float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

			// accelerate or decelerate to target speed
			if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
			{
				// creates curved result rather than a linear one giving a more organic speed change
				// note T in Lerp is clamped, so we don't need to clamp our speed
				_speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);

				// round speed to 3 decimal places
				_speed = Mathf.Round(_speed * 1000f) / 1000f;
			}
			else
			{
				_speed = targetSpeed;
			}

			// normalise input direction
			Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

			// note: Vector2's != operator uses approximation so is not floating point error prone, and is cheaper than magnitude
			// if there is a move input rotate player when the player is moving
			if (_input.move != Vector2.zero)
			{
				// move
				inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
			}

			// move the player
			Vector3 step = inputDirection.normalized * (_speed * Time.deltaTime);
			float stepOffset = _controller.stepOffset;
			if (_input.move != Vector2.zero)
			{
				// 角をよける横の動きは一歩の中から出す（前へ進む分と足して一歩より速くならないように）
				float len = step.magnitude;
				Vector3 nudge = CornerNudge(inputDirection.normalized, len);
				if (len > 1e-6f && nudge != Vector3.zero) step = step * (1f - Mathf.Min(1f, nudge.magnitude / len)) + nudge;
				ShovePushables(step);
				// 押せる家具へ向かう時は段差として乗り上げない（椅子の座面に乗って越えてしまうので、押して進む）
				if (PushableAhead(inputDirection.normalized, step.magnitude)) _controller.stepOffset = 0f;
			}
			_controller.Move(step + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
			_controller.stepOffset = stepOffset;
		}

		/// <summary>歩く先（体の形のまま）に押せる家具があるか</summary>
		private bool PushableAhead(Vector3 dir, float step)
		{
			float r = _controller.radius;
			Vector3 c = transform.TransformPoint(_controller.center);
			Vector3 half = Vector3.up * Mathf.Max(0f, _controller.height * 0.5f - r);
			int count = Physics.CapsuleCastNonAlloc(c - half, c + half, r, dir, _castHits, step + _controller.skinWidth + 0.05f,
				CollideMask, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				var body = _castHits[i].collider.attachedRigidbody;
				if (body != null && (!body.isKinematic || TryPushable(_castHits[i].collider, out _))) return true;
			}
			return false;
		}

		/// <summary>押せる家具（PushableProp）の当たり判定か</summary>
		private static bool TryPushable(Collider c, out EscapeProto.PushableProp prop)
		{
			prop = null;
			var body = c.attachedRigidbody;
			return body != null && body.TryGetComponent(out prop) && prop.isActiveAndEnabled;
		}

		/// <summary>
		/// この一歩（step）で体が当たる押せる家具を、先にどかしておく。当たる面の向きへ、めり込む分だけ押す
		///（正面から当たれば歩く向きへ、斜めにかすれば横へ逃げる）。どかし切れない時（壁際・机から離れてよい距離の端）は
		/// その分だけ体が当たって止まり、CornerNudge と CharacterController が横へよけさせる
		/// </summary>
		private void ShovePushables(Vector3 step)
		{
			Vector3 h = new Vector3(step.x, 0f, step.z);
			float len = h.magnitude;
			if (len < 1e-6f) return;
			Vector3 dir = h / len;
			float r = _controller.radius;
			Vector3 c = transform.TransformPoint(_controller.center);
			Vector3 half = Vector3.up * Mathf.Max(0f, _controller.height * 0.5f - r);
			float dist = len + _controller.skinWidth + ShoveMargin;
			// 1回に1か所ずつどかして調べ直す（座面と背もたれのように、同じ椅子の2か所に当たる時もある）
			for (int iter = 0; iter < 4; iter++)
			{
				int count = Physics.CapsuleCastNonAlloc(c - half, c + half, r, dir, _castHits, dist, CollideMask, QueryTriggerInteraction.Ignore);
				EscapeProto.PushableProp target = null;
				RaycastHit nearest = default;
				for (int i = 0; i < count; i++)
				{
					var hit = _castHits[i];
					if (!TryPushable(hit.collider, out var prop)) continue;
					if (target == null || hit.distance < nearest.distance) { target = prop; nearest = hit; }
				}
				if (target == null) return;
				// どかす向き：正面寄りに当たったら歩く向きへ（斜めに置いた椅子が面の向きへずれて机に寄らないように）、
				// 斜めにかすったら当たる面の向きへ（横へ逃げて体が通り過ぎる）。間はなめらかに混ぜる。最初から触れている時は歩く向き
				Vector3 n = Vector3.ProjectOnPlane(nearest.normal, Vector3.up);
				Vector3 face = dir;   // 押される面の奥向き
				if (nearest.distance > 0f && n.sqrMagnitude > 1e-4f) face = -n.normalized;
				float into = Vector3.Dot(dir, face);
				if (into <= 0.01f) return;   // 離れていく向き
				float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ShoveGlanceCos, ShoveHeadOnCos, into));
				Vector3 pushDir = Vector3.Slerp(face, dir, w).normalized;
				// 面をこの向きに動かして、一歩の分だけ面が退く量（面の向きへなら深さそのもの、歩く向きへなら残りの一歩）
				float along = Vector3.Dot(pushDir, face);
				float depth = (dist - nearest.distance) * into / Mathf.Max(along, 0.1f);
				if (depth <= 1e-5f || target.Shove(pushDir * depth) <= 1e-5f) return;
			}
		}

		/// <summary>
		/// 進む先がふさがっている時、横へ少しずれれば抜けられる（角や細い物にかすっているだけ）なら、その向きへの1フレーム分の移動を返す。
		/// 斜めに当たる時は CharacterController がそのまま滑らせるので何もしない
		/// </summary>
		private Vector3 CornerNudge(Vector3 dir, float step)
		{
			if (step <= 1e-5f) return Vector3.zero;
			float r = _controller.radius;
			Vector3 c = transform.TransformPoint(_controller.center);
			float half = Mathf.Max(0f, _controller.height * 0.5f - r);
			Vector3 top = c + Vector3.up * half;
			Vector3 bottom = c - Vector3.up * Mathf.Max(0f, half - _controller.stepOffset);   // 越えられる段差は障害物に数えない
			if (bottom.y > top.y) bottom = top;
			float look = step + _controller.skinWidth + 0.05f;
			if (!Cast(bottom, top, r, dir, look, out var hit)) return Vector3.zero;
			Vector3 n = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
			if (n.sqrMagnitude < 1e-4f || Vector3.Dot(n.normalized, -dir) < CornerCos) return Vector3.zero;

			// 当たった所と反対の側から試す（右にかすったら左へ）
			Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
			float first = Vector3.Dot(hit.point - c, side) > 0f ? -1f : 1f;
			float probe = Mathf.Max(look, 0.3f);
			for (float s = 0.03f; s <= CornerAssist + 1e-4f; s += 0.03f)
				for (int k = 0; k < 2; k++)
				{
					Vector3 o = side * (k == 0 ? first : -first);
					if (Cast(bottom, top, r, o, s, out _)) continue;                    // 横へずれる所がふさがっている
					if (Cast(bottom + o * s, top + o * s, r, dir, probe, out _)) continue;   // ずれても前がふさがっている
					return o * Mathf.Min(s, step);
				}
			return Vector3.zero;
		}

		/// <summary>体の形（少し細め）を dist だけ動かした時に、プレイヤーが当たる物に当たるか（一番近いもの）</summary>
		private bool Cast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float dist, out RaycastHit nearest)
		{
			nearest = default;
			int count = Physics.CapsuleCastNonAlloc(bottom, top, radius * 0.95f, dir, _castHits, dist, CollideMask, QueryTriggerInteraction.Ignore);
			bool any = false;
			for (int i = 0; i < count; i++)
			{
				var h = _castHits[i];
				if (h.collider == _controller || h.distance <= 0f) continue;   // 自分・最初から触れている物（横の壁など）は数えない
				// 押せる家具はよけずに押して進む（壁際や、机から離れてよい距離の端で動けない時は障害物として回り込む）
				if (TryPushable(h.collider, out var prop)) { if (prop.CanYield(dir)) continue; }
				else
				{
					var body = h.collider.attachedRigidbody;
					if (body != null && !body.isKinematic) continue;
				}
				if (!any || h.distance < nearest.distance) { nearest = h; any = true; }
			}
			return any;
		}

		/// <summary>プレイヤーが当たる層</summary>
		private int CollideMask
		{
			get
			{
				if (_collideMask == -1)
				{
					_collideMask = 0;
					for (int i = 0; i < 32; i++)
						if (!Physics.GetIgnoreLayerCollision(gameObject.layer, i)) _collideMask |= 1 << i;
				}
				return _collideMask;
			}
		}

		/// <summary>
		/// 動く Rigidbody（壊れた破片など）に当たったら、歩く向きへ押して床の上を滑らせる。正面から当たるほど強く押す。
		/// 足もとの物・動かない物は押さない。押せる家具（PushableProp）は Kinematic なのでここでは押さない（ShovePushables）
		/// </summary>
		private void OnControllerColliderHit(ControllerColliderHit hit)
		{
			var body = hit.collider.attachedRigidbody;
			if (body == null || body.isKinematic || hit.moveDirection.y < -0.3f) return;
			Vector3 dir = Vector3.ProjectOnPlane(hit.moveDirection, Vector3.up);
			Vector3 n = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
			if (dir.sqrMagnitude < 1e-4f || n.sqrMagnitude < 1e-4f) return;
			dir.Normalize();
			float into = Mathf.Clamp01(Vector3.Dot(dir, -n.normalized));
			float speed = PushSpeed * Mathf.Max(into, 0.35f);
			Vector3 v = body.linearVelocity;
			float along = Vector3.Dot(v, dir);
			if (along < speed) body.linearVelocity = v + dir * (speed - along);
		}

		private void JumpAndGravity()
		{
			if (Grounded)
			{
				// reset the fall timeout timer
				_fallTimeoutDelta = FallTimeout;

				// stop our velocity dropping infinitely when grounded
				if (_verticalVelocity < 0.0f)
				{
					_verticalVelocity = -2f;
				}

				// Jump
				if (_input.jump && _jumpTimeoutDelta <= 0.0f)
				{
					// the square root of H * -2 * G = how much velocity needed to reach desired height
					_verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
				}

				// jump timeout
				if (_jumpTimeoutDelta >= 0.0f)
				{
					_jumpTimeoutDelta -= Time.deltaTime;
				}
			}
			else
			{
				// reset the jump timeout timer
				_jumpTimeoutDelta = JumpTimeout;

				// fall timeout
				if (_fallTimeoutDelta >= 0.0f)
				{
					_fallTimeoutDelta -= Time.deltaTime;
				}

				// if we are not grounded, do not jump
				_input.jump = false;
			}

			// apply gravity over time if under terminal (multiply by delta time twice to linearly speed up over time)
			if (_verticalVelocity < _terminalVelocity)
			{
				_verticalVelocity += Gravity * Time.deltaTime;
			}
		}

		private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}
	}
}