const API = (window.API_URL || "http://localhost:5000").replace(/\/$/, "");
const emptyDonor = () => ({ id: 0, name: "", age: 18, phone: "", gender: "Male", bloodType: "A+", address: "" });
const emptyStock = () => ({ id: 0, donorName: "", bloodType: "A+", units: 1 });
const emptyRequest = () => ({ id: 0, patientName: "", hospitalName: "", bloodType: "A+", units: 1, status: "Pending" });
const emptyTransfer = () => ({ patientName: "", hospitalName: "", bloodType: "A+", units: 1 });

Vue.createApp({
  components: {
    DataTable: {
      props: ["rows"],
      emits: ["edit", "delete"],
      methods: {
        label(key) { return key.replaceAll("_", " "); },
        show(v) {
          if (v === null || v === undefined || v === "") return "-";
          if (typeof v === "string" && /^\d{4}-\d{2}-\d{2}T/.test(v)) return new Date(v).toLocaleString();
          return v;
        }
      },
      template: `
        <div class="table-wrap" v-if="rows && rows.length">
          <table>
            <thead><tr><th v-for="(_, key) in rows[0]" :key="key">{{ label(key) }}</th><th v-if="$attrs.onEdit || $attrs.onDelete">Action</th></tr></thead>
            <tbody>
              <tr v-for="row in rows" :key="row.id">
                <td v-for="(cell, key) in row" :key="key">{{ show(cell) }}</td>
                <td v-if="$attrs.onEdit || $attrs.onDelete">
                  <button class="ghost" v-if="$attrs.onEdit" @click="$emit('edit', row)">Edit</button>
                  <button class="primary" v-if="$attrs.onDelete" @click="$emit('delete', row.id)">Delete</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="muted" v-else>No records found.</p>`
    }
  },
  data() {
    return {
      user: JSON.parse(localStorage.getItem("bb_user") || "null"),
      auth: { name: "", password: "" },
      apiOnline: false,
      error: "",
      publicPage: "home",
      page: "dashboard",
      menu: [
        { key: "dashboard", label: "Dashboard", icon: "01" },
        { key: "donors", label: "Donors", icon: "02" },
        { key: "stock", label: "Stock", icon: "03" },
        { key: "requests", label: "Requests", icon: "04" },
        { key: "transfers", label: "Transfers", icon: "05" },
        { key: "reports", label: "Reports", icon: "06" }
      ],
      bloodTypes: ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"],
      dashboard: {},
      donors: [],
      stock: [],
      requests: [],
      reports: [],
      donorForm: emptyDonor(),
      publicDonor: emptyDonor(),
      publicDonorSaved: false,
      stockForm: emptyStock(),
      requestForm: emptyRequest(),
      transferForm: emptyTransfer()
    };
  },
  computed: {
    currentTitle() {
      return this.menu.find(item => item.key === this.page)?.label || "Dashboard";
    },
    totalAvailableUnits() {
      return this.stock.reduce((sum, item) => sum + Number(item.units || 0), 0);
    },
    bloodInventory() {
      return this.bloodTypes.map(type => {
        const units = this.stock
          .filter(item => this.readBloodType(item) === type)
          .reduce((sum, item) => sum + Number(item.units || 0), 0);
        const level = units === 0 ? "empty" : units < 5 ? "low" : units < 10 ? "stable" : "strong";
        const label = units === 0 ? "Empty" : units < 5 ? "Low stock" : units < 10 ? "Stable" : "Healthy";
        return { type, units, level, label };
      });
    },
    pendingRequests() {
      return this.requests.filter(item => String(item.status || "").toLowerCase() !== "completed");
    },
    priorityAlerts() {
      const alerts = [];
      this.bloodInventory
        .filter(item => item.units < 5)
        .forEach(item => alerts.push({
          title: `${item.type} stock needs attention`,
          message: `${item.units} unit(s) available. Add stock before issuing more transfers.`,
          level: item.units === 0 ? "critical" : "warning",
          levelLabel: item.units === 0 ? "Critical" : "Low"
        }));
      this.pendingRequests.slice(0, 3).forEach(request => alerts.push({
        title: `${request.blood_type} request from ${request.hospital_name}`,
        message: `${request.patient_name} needs ${request.units} unit(s). Check stock and issue if available.`,
        level: "info",
        levelLabel: "Request"
      }));
      return alerts.slice(0, 6);
    }
  },
  async mounted() {
    await this.checkApi();
    if (this.user && this.apiOnline) await this.loadAll();
  },
  methods: {
    async checkApi() {
      try {
        const res = await fetch(API + "/");
        this.apiOnline = res.ok;
        if (!res.ok) this.error = `API check failed: ${res.status} ${res.statusText}`;
      } catch (e) {
        this.apiOnline = false;
        this.error = `Cannot reach API: ${API}. ${e.message || "Check Render and CORS."}`;
      }
    },
    async call(path, options = {}) {
      this.error = "";
      let res;
      try {
        res = await fetch(API + path, { headers: { "Content-Type": "application/json" }, ...options });
      } catch (e) {
        this.apiOnline = false;
        throw new Error(`Cannot reach API: ${API}. ${e.message || "Check Render and CORS."}`);
      }
      this.apiOnline = true;
      const text = await res.text();
      let data = null;
      try {
        data = text ? JSON.parse(text) : null;
      } catch {
        data = { error: text };
      }
      if (!res.ok) throw new Error(data?.error || `API error: ${res.status} ${res.statusText}.`);
      return data;
    },
    async login() {
      try {
        this.user = await this.call("/api/login", { method: "POST", body: JSON.stringify(this.auth) });
        localStorage.setItem("bb_user", JSON.stringify(this.user));
        await this.loadAll();
      } catch (e) { this.error = e.message; }
    },
    async register() {
      try {
        await this.call("/api/register", { method: "POST", body: JSON.stringify(this.auth) });
        await this.login();
      } catch (e) { this.error = e.message; }
    },
    async submitPublicDonor() {
      try {
        await this.call("/api/donors", { method: "POST", body: JSON.stringify(this.publicDonor) });
        this.publicDonor = emptyDonor();
        this.publicDonorSaved = true;
      } catch (e) { this.error = e.message; }
    },
    logout() { localStorage.removeItem("bb_user"); this.user = null; this.page = "dashboard"; this.publicPage = "home"; },
    async openPage(page) { this.page = page; await this.loadAll(); },
    async loadAll() {
      try {
        [this.dashboard, this.donors, this.stock, this.requests, this.reports] = await Promise.all([
          this.call("/api/dashboard"), this.call("/api/donors"), this.call("/api/stock"), this.call("/api/requests"), this.call("/api/reports")
        ]);
      } catch (e) { this.error = e.message; }
    },
    readBloodType(item) {
      return item.blood_type || item.bloodType || item.BloodType || "";
    },
    async save(type, form) {
      try {
        const path = `/api/${type}` + (form.id ? `/${form.id}` : "");
        await this.call(path, { method: form.id ? "PUT" : "POST", body: JSON.stringify(form) });
        this.resetDonor(); this.resetStock(); this.resetRequest();
        await this.loadAll();
      } catch (e) { this.error = e.message; }
    },
    async remove(type, id) {
      if (!confirm("Delete this record?")) return;
      try { await this.call(`/api/${type}/${id}`, { method: "DELETE" }); await this.loadAll(); }
      catch (e) { this.error = e.message; }
    },
    async issueBlood() {
      try {
        await this.call("/api/transfers", { method: "POST", body: JSON.stringify(this.transferForm) });
        this.transferForm = emptyTransfer();
        this.page = "reports";
        await this.loadAll();
      } catch (e) { this.error = e.message; }
    },
    editDonor(row) { this.donorForm = { id: row.id, name: row.name, age: row.age, phone: row.phone, gender: row.gender, bloodType: row.blood_type, address: row.address }; },
    editStock(row) { this.stockForm = { id: row.id, donorName: row.donor_name, bloodType: row.blood_type, units: row.units }; },
    editRequest(row) { this.requestForm = { id: row.id, patientName: row.patient_name, hospitalName: row.hospital_name, bloodType: row.blood_type, units: row.units, status: row.status }; },
    resetDonor() { this.donorForm = emptyDonor(); },
    resetStock() { this.stockForm = emptyStock(); },
    resetRequest() { this.requestForm = emptyRequest(); }
  }
}).mount("#app");
