"""Fast numerical adapter tests; these do not claim pretrained generation."""
import unittest

import torch

import amd_backend


class AdapterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        amd_backend.register_source()
        cls.original = staticmethod(amd_backend.install_chunked_attention(16))

    def test_chunked_attention_cpu(self):
        torch.manual_seed(42)
        q = torch.randn(1, 4, 39, 16)
        k = torch.randn(1, 4, 47, 16)
        v = torch.randn_like(k)
        mask = torch.randn(39, 47)
        for attn_mask in (None, mask):
            actual = torch.nn.functional.scaled_dot_product_attention(q, k, v, attn_mask=attn_mask)
            expected = self.original(q, k, v, attn_mask=attn_mask)
            torch.testing.assert_close(actual, expected)

    def test_chunked_attention_directml(self):
        import torch_directml
        device = torch_directml.device()
        torch.manual_seed(42)
        q = torch.randn(1, 4, 39, 16).half()
        k = torch.randn(1, 4, 47, 16).half()
        v = torch.randn_like(k)
        expected = self.original(q, k, v)
        actual = torch.nn.functional.scaled_dot_product_attention(q.to(device), k.to(device), v.to(device)).cpu()
        torch.testing.assert_close(actual, expected, atol=.003, rtol=.01)

    def test_meta_load_all_inference_components(self):
        from hy3dshape.pipelines import instantiate_from_config
        specs = [
            {'target': 'hy3dshape.models.denoisers.hunyuandit.HunYuanDiTPlain', 'params': dict(
                input_size=32, in_channels=8, context_dim=32, hidden_size=64, num_heads=4,
                depth=3, text_len=17, use_attention_pooling=False, qk_norm=True,
                qk_norm_type='rms', qkv_bias=False, num_moe_layers=1, num_experts=4, moe_top_k=2)},
            {'target': 'hy3dshape.models.autoencoders.ShapeVAE', 'params': dict(
                num_latents=32, embed_dim=8, width=64, heads=4, num_decoder_layers=1,
                num_encoder_layers=1, pc_size=64, pc_sharpedge_size=0,
                downsample_ratio=2, num_freqs=4, include_pi=False, qkv_bias=False, qk_norm=True)},
            {'target': 'hy3dshape.models.conditioner.SingleImageEncoder', 'params': dict(
                main_image_encoder={'type': 'DinoImageEncoder', 'kwargs': dict(image_size=56,
                    config=dict(hidden_size=32, image_size=56, patch_size=14,
                        num_attention_heads=4, num_hidden_layers=2, mlp_ratio=4, hidden_act='gelu',
                        attention_probs_dropout_prob=0., hidden_dropout_prob=0., use_swiglu_ffn=False))})}
        ]
        for spec in specs:
            original = instantiate_from_config(spec).eval()
            loaded = amd_backend.make_component(spec, original.state_dict(), 'cpu', torch.float32)
            self.assertFalse(any(p.is_meta for p in loaded.parameters()))
            for key, value in original.state_dict().items():
                torch.testing.assert_close(loaded.state_dict()[key], value)

    def test_flow_scheduler_directml(self):
        import numpy as np
        import torch_directml
        from hy3dshape.schedulers import FlowMatchEulerDiscreteScheduler
        device = torch_directml.device()
        cpu, amd = FlowMatchEulerDiscreteScheduler(), FlowMatchEulerDiscreteScheduler()
        for scheduler in (cpu, amd):
            scheduler.set_timesteps(sigmas=np.linspace(0, 1, 4), device='cpu')
        latent_cpu = torch.randn(1, 32, 8).half()
        latent_amd = latent_cpu.to(device)
        for timestep_cpu, timestep_amd in zip(cpu.timesteps, amd.timesteps):
            prediction = torch.randn_like(latent_cpu)
            latent_cpu = cpu.step(prediction, timestep_cpu, latent_cpu).prev_sample
            latent_amd = amd.step(prediction.to(device), timestep_amd, latent_amd).prev_sample
        torch.testing.assert_close(latent_amd.cpu(), latent_cpu, atol=.004, rtol=.01)

    def test_sequential_block_offload(self):
        import torch_directml
        from hy3dshape.pipelines import instantiate_from_config
        spec = {'target': 'hy3dshape.models.denoisers.hunyuandit.HunYuanDiTPlain', 'params': dict(
            input_size=32, in_channels=8, context_dim=32, hidden_size=64, num_heads=4,
            depth=3, text_len=17, use_attention_pooling=False, qk_norm=True,
            qk_norm_type='rms', qkv_bias=False, num_moe_layers=1, num_experts=4, moe_top_k=2)}
        model = instantiate_from_config(spec).eval().half()
        x, t = torch.randn(1, 32, 8).half(), torch.tensor([.3]).half()
        cond = {'main': torch.randn(1, 17, 32).half()}
        with torch.inference_mode():
            expected = model(x, t, cond)
            device = torch_directml.device()
            handles = amd_backend.offload_dit_blocks(model, device)
            for _ in range(2):
                actual = model(x.to(device), t.to(device), {'main': cond['main'].to(device)}).cpu()
                torch.testing.assert_close(actual, expected, atol=.015, rtol=.03)
                self.assertTrue(all(p.device.type == 'cpu' for block in model.blocks for p in block.parameters()))
            for handle in handles:
                handle.remove()

    def test_bounded_volume_grid(self):
        from hy3dshape.models.autoencoders.volume_decoders import VanillaVolumeDecoder
        def field(queries, latents):
            return queries.square().sum(-1, keepdim=True) + latents.mean()
        latents = torch.ones(1, 2, 3)
        options = dict(bounds=1.01, octree_resolution=5, num_chunks=7, enable_pbar=False)
        expected = VanillaVolumeDecoder()(latents, field, **options)
        actual = amd_backend.BoundedVolumeDecoder()(latents, field, **options)
        torch.testing.assert_close(actual, expected)


if __name__ == '__main__':
    unittest.main(verbosity=2)
