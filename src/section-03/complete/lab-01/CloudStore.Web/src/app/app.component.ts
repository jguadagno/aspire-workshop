import { Component, OnInit, ChangeDetectionStrategy } from '@angular/core';

import { FormsModule } from '@angular/forms';
import { ProductService } from './services/product.service';
import { Product } from './models/product.model';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="container">
      <h1>CloudStore - Products</h1>
    
      <div class="add-product">
        @if (editingProduct) {
          <h2>Edit Product</h2>
          <form (ngSubmit)="saveProduct()">
            <input [(ngModel)]="editingProduct.name" name="name" placeholder="Name" required>
            <input [(ngModel)]="ngModelBinding" name="description" placeholder="Description" (input)="updateEditingDescription($event)">
            <input [(ngModel)]="editingProduct.price" name="price" placeholder="Price" type="number" required>
            <button type="submit">Save Changes</button>
            <button type="button" (click)="cancelEdit()" class="cancel">Cancel</button>
          </form>
        } @else {
          <h2>Add New Product</h2>
          <form (ngSubmit)="addProduct()">
            <input [(ngModel)]="newProduct.name" name="name" placeholder="Name" required>
            <input [(ngModel)]="newProduct.description" name="description" placeholder="Description">
            <input [(ngModel)]="newProduct.price" name="price" placeholder="Price" type="number" required>
            <button type="submit">Add Product</button>
          </form>
        }
      </div>
    
      <div class="products-list">
        <h2>Products</h2>
        @if (loading) {
          <div class="loading">Loading...</div>
        }
        @if (!loading && products.length === 0) {
          <div>No products found</div>
        }
        @for (product of products; track product) {
          <div class="product-card">
            <h3>{{ product.name }}</h3>
            <p>{{ product.description }}</p>
            <p class="price">\${{ product.price }}</p>
            @if (product.imageUrl) {
              <img [src]="product.imageUrl" alt="{{ product.name }}" class="thumbnail">
            }
            <div class="actions">
              <button (click)="editProduct(product)">Edit</button>
              <button (click)="deleteProduct(product.id)" class="delete">Delete</button>
              <input type="file" #fileInput (change)="uploadImage($event, product.id)">
              <button (click)="fileInput.click()">Upload Image</button>
            </div>
          </div>
        }
      </div>
    </div>
    `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: [`
    .container { max-width: 1200px; margin: 0 auto; padding: 20px; font-family: Arial, sans-serif; }
    h1 { color: #333; }
    .add-product { background: #f5f5f5; padding: 20px; border-radius: 5px; margin-bottom: 20px; }
    input, button { padding: 8px; margin: 5px; font-size: 14px; }
    button { background: #007bff; color: white; border: none; cursor: pointer; border-radius: 3px; }
    button:hover { background: #0056b3; }
    button.cancel { background: #6c757d; }
    button.cancel:hover { background: #5a6268; }
    button.delete { background: #dc3545; }
    button.delete:hover { background: #c82333; }
    .products-list { display: grid; gap: 20px; }
    .product-card { border: 1px solid #ddd; padding: 15px; border-radius: 5px; }
    .price { font-weight: bold; color: #28a745; }
    .thumbnail { max-width: 200px; border-radius: 3px; margin: 10px 0; }
    .actions { margin-top: 10px; }
    input[type="file"] { display: none; }
    .loading { text-align: center; color: #666; }
  `]
})
export class AppComponent implements OnInit {
  products: Product[] = [];
  newProduct: Product = { id: 0, name: '', description: '', price: 0, imageUrl: '' };
  editingProduct: Product | null = null;
  ngModelBinding: string = '';
  loading = true;

  constructor(private productService: ProductService) {}

  ngOnInit() {
    this.loadProducts();
  }

  loadProducts() {
    this.loading = true;
    this.productService.getProducts().subscribe({
      next: (data) => {
        this.products = data;
        this.loading = false;
      },
      error: () => {
        console.error('Failed to load products');
        this.loading = false;
      }
    });
  }

  addProduct() {
    if (!this.newProduct.name || !this.newProduct.price) return;
    this.productService.createProduct(this.newProduct).subscribe({
      next: (created) => {
        this.products.push(created);
        this.newProduct = { id: 0, name: '', description: '', price: 0, imageUrl: '' };
      }
    });
  }

  editProduct(product: Product) {
    this.editingProduct = { ...product };
    this.ngModelBinding = product.description || '';
  }

  updateEditingDescription(event: any) {
    if (this.editingProduct) {
      this.editingProduct.description = event.target.value;
    }
  }

  saveProduct() {
    if (!this.editingProduct || !this.editingProduct.name || !this.editingProduct.price) return;
    this.productService.updateProduct(this.editingProduct.id, this.editingProduct).subscribe({
      next: (updated) => {
        const index = this.products.findIndex(p => p.id === updated.id);
        if (index !== -1) {
          this.products[index] = updated;
        }
        this.editingProduct = null;
      }
    });
  }

  cancelEdit() {
    this.editingProduct = null;
  }

  deleteProduct(id: number) {
    this.productService.deleteProduct(id).subscribe(() => {
      this.products = this.products.filter(p => p.id !== id);
    });
  }

  uploadImage(event: Event, productId: number) {
    const input = event.target as HTMLInputElement;
    if (input.files && input.files[0]) {
      this.productService.uploadImage(productId, input.files[0]).subscribe({
        next: () => this.loadProducts()
      });
    }
  }
}
